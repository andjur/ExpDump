using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace ExpDump
{
    internal class Program
    {
        static void Main(string[] args)
        {
            try
            {
                Console.WriteLine("Exposures Dumper v" + Assembly.GetExecutingAssembly().GetName().Version);
                Console.WriteLine();

                string dir = String.Empty;
                IEnumerable<string>? files = null;

                switch (args.Length)
                {
                    case 0:
                        // DEFAULT: read files list from current working directory
                        dir = Directory.GetCurrentDirectory();
                        break;
                    case 1:
                        if (File.Exists(args[0]))
                        {
                            // SPECIAL CASE: read files list from file (use for devel and debug)
                            var file = args[0];
                            Console.WriteLine("SPECIAL CASE: Processing list of file names from \"" + file + "\".");
                            Console.WriteLine("Reading file names from file...");
                            files = File.ReadLines(file).ToArray();
                            Console.WriteLine("Done reading file names from file. Read " + files.Count().ToString() + " file names.");
                        }
                        else
                        {
                            if (Directory.Exists(args[0]))
                            {
                                // NORMAL CASE: process given directory
                                dir = args[0];
                            }
                            else
                            {
                                Console.WriteLine("ERROR: Specified file or directory does not exist.");
                                Console.WriteLine();
                            }
                        }
                        break;
                    default:
                        Console.WriteLine("ERROR: Don't know what to do. Exiting program.");
                        Console.WriteLine();

                        Usage();
                        break;
                }

                if (!String.IsNullOrEmpty(dir))
                {
                    Console.WriteLine("Searching directory \"" + dir + "\" for files...");
                    files = Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories).ToArray();
                    //File.WriteAllText("__ExpDump_" + Timestamp() + ".lst", String.Join('\n', files));
                    Console.WriteLine("Done searching directory \"" + dir + "\" for files. Found " + files.Count().ToString() + " file(s).");
                }

                if (files != null)
                {
                    Console.WriteLine("Processing " + files.Count().ToString() + " file(s)...");
                    var res = ProcessFileNamesList(files);
                    var resultsFile = Path.GetFullPath("__ExpDump_" + Timestamp() + ".csv");
                    Console.WriteLine("Writing results to \"" + resultsFile + "\"...");
                    File.WriteAllText(resultsFile, res);
                    Console.WriteLine("Done writing results to \"" + resultsFile + "\".");
                    Console.WriteLine("Done processing " + files.Count().ToString() + " file(s).");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }

            Console.WriteLine();
            Console.WriteLine("Press any key to exit.");
            Console.ReadKey();
        }

        private static string ProcessFileNamesList(IEnumerable<string> fileNames)
        {
            var shootings = new Dictionary<string, ShootingInfo>(); // TKey is shooting (i.e. date/object/telescope/camera/filter/exp_duration combination)
            foreach (string fileName in fileNames)
            {
                var r = new Regex(@"(?<Path>.*\\)Light_(?<ObjectName>.*?)_.*?(?<ExposureDuration>.*?s)_.+?_(?<Camera>.+?)_.*(?<ExposureEndDateTime>\d{4}\d{2}\d{2}-\d{2}\d{2}\d{2}).*?_(filter_(?<Filter>.+?)_)?\d\d\d\d");
                var match = r.Match(fileName);
                if (match.Success)
                {
                    // we have a sub
                    var subInfo = new SubInfo()
                    {
                        Path = match.Groups["Path"].Value,
                        ObjectName = NormalizeObjectName(match.Groups["ObjectName"].Value),
                        ExposureDurationStr = match.Groups["ExposureDuration"].Value.Replace(".0ms", "ms").Replace(".0s", "s"),
                        Camera = match.Groups["Camera"].Value,
                        ExposureEndDateTimeStr = match.Groups["ExposureEndDateTime"].Value,
                        Filter = String.IsNullOrEmpty(match.Groups["Filter"].Value) ? "unknown_filter" : match.Groups["Filter"].Value,
                        Telescope = DetectTelescope(match.Groups["Path"].Value, match.Groups["ExposureEndDateTime"].Value),
                        Session = GetSession(match.Groups["Path"].Value),
                    };

                    if (!subInfo.IsBad)
                    {
                        // we have a "good" (i.e. not BAD) sub
                        var shootingKey = new ShootingKey() {
                            NormalizedExposureDate = subInfo.NormalizedExposureDate,
                            ObjectName = subInfo.ObjectName,
                            Camera = subInfo.Camera,
                            Filter = subInfo.Filter,
                            ExposureDurationStr = subInfo.ExposureDurationStr,
                            Telescope = subInfo.Telescope,
                            Session = subInfo.Session,
                        };

                        // include sub into shooting stats
                        var key = shootingKey.ToString();
                        if (shootings.ContainsKey(key))
                        {
                            var shootingInfo = shootings[key];
                            shootingInfo.SubsCount++;
                            if (subInfo.ExposureStartDateTime < shootingInfo.StartDateTime)
                                shootingInfo.StartDateTime = subInfo.ExposureStartDateTime;
                            if (subInfo.ExposureEndDateTime > shootingInfo.EndDateTime)
                                shootingInfo.EndDateTime = subInfo.ExposureEndDateTime;
                        }
                        else
                        {
                            shootings[key] = new ShootingInfo()
                            {
                                SubsCount = 1,
                                StartDateTime = subInfo.ExposureStartDateTime,
                                EndDateTime = subInfo.ExposureEndDateTime,
                            };
                        }
                    }
                }
            }

            var sb = new StringBuilder();
            var sbSep = ";";
            sb.AppendLine(String.Join(sbSep,
                "Normalized Date",
                "Object",
                "Telescope",
                "Camera",
                "Filter",
                "Start Date",
                "Start Time",
                "End Date",
                "End Time",
                "Total Time",
                "Subs Count",
                "Sub Duration",
                "Details",
                "Session"
            ));
            foreach (var key in shootings.Keys.OrderBy(key => key).OrderBy(key => shootings[key].StartDateTime))
            {
                var k = ShootingKey.FromString(key);
                var shootingInfo = shootings[key];
                var shootingSeconds = k.ExposureDurationMilliseconds  * shootingInfo.SubsCount / 1000;
                var shootingHours = shootingSeconds / 3600;
                var shootingMinutesFracion = (shootingSeconds - shootingHours * 3600) / 60;
                var idleTimeSeconds = (shootingInfo.EndDateTime - shootingInfo.StartDateTime).TotalSeconds - shootingSeconds;
                //Console.WriteLine(
                sb.AppendLine(
                    String.Join(sbSep,
                        k.NormalizedExposureDate.ToString("yyyy-MM-dd"),
                        k.ObjectName,
                        k.Telescope,
                        NormalizeCamera(k.Camera),
                        k.Filter,
                        shootingInfo.StartDateTime.ToString("yyyy-MM-dd"),
                        shootingInfo.StartDateTime.ToString("H:mm"), //shootingInfo.StartDateTime.ToString("yyyy-MM-dd H:mm"),
                        shootingInfo.EndDateTime.ToString("yyyy-MM-dd"),
                        shootingInfo.EndDateTime.ToString("H:mm"), //shootingInfo.EndDateTime.ToString("yyyy-MM-dd H:mm"),
                                                                   //"idle: "+(idleTimeSeconds/60).ToString("N1")+" minutes",
                        shootingHours.ToString() + ":" + shootingMinutesFracion.ToString("D2"),
                        shootingInfo.SubsCount.ToString(),
                        k.ExposureDurationStr,
                        String.Join(" ",
                            k.Filter,
                            shootingInfo.SubsCount.ToString() + "x" + k.ExposureDurationStr,
                            "(" + shootingHours.ToString() + ":" + shootingMinutesFracion.ToString("D2") + ")"
                        ),
                        k.Session
                    )
                );
            }

            return sb.ToString();
        }

        private static string NormalizeObjectName(string name)
        {
            // SPECIAL CASE: Caldwell catalog object -> normalize name (special treatment to avoid confusion with "C" comets)
            var caldwellRegex = new Regex(@"^(?<CaldwellObject>C)\s*?(?<CatalogNumber>\d+)$", RegexOptions.IgnoreCase);
            var caldwellMatch = caldwellRegex.Match(name);
            if (caldwellMatch.Success)
                return caldwellMatch.Groups["CaldwellObject"].Value.ToUpper() + " " + caldwellMatch.Groups["CatalogNumber"].Value;

            // SPECIAL CASE: comets - do NOT normalize, keep original name
            //var cometRegex = new Regex(@".*?(?<Type>C|P|I)(\s*|-).*", RegexOptions.IgnoreCase);
            //var cometMatch = cometRegex.Match(name);
            //if (cometMatch.Success)
            //    return name;

            // catalog object -> normalize name
            var r = new Regex(@"^(?<Catalog>M|NGC|IC|LDN|LBN|SH)\s*(?<CatalogNumber>.+)", RegexOptions.IgnoreCase);
            var match = r.Match(name);
            if (match.Success)
                return match.Groups["Catalog"].Value.ToUpper() + " " + match.Groups["CatalogNumber"].Value;

            // unrecognized object -> keep original name
            return name;
        }

        private static string DetectTelescope(string path, string exposureEndDateTime)
        {
            var res = "unknown_telescope";

            var r = new Regex(@"(?<Telescope>C9\.25|SQA55|Samyang|AllSky)");
            var match = r.Match(path);
            if (match.Success)
            {
                res = match.Groups["Telescope"].Value;
            }
            else if (SubInfo.ExtractExposureDateTime(exposureEndDateTime) < new DateTime(2024, 12, 25))
                res = "C9.25"; // SPECIAL CASE: before 2024-12-25 only C9.25 was available

            r = new Regex(@"(?<Reducer>Hyperstar|0\.7x)", RegexOptions.IgnoreCase);
            match = r.Match(path);
            if (match.Success)
            {
                var reducer = match.Groups["Reducer"].Value;
                if (!String.IsNullOrWhiteSpace(reducer))
                {
                    res += "+" + reducer;
                }
            }

            return res;
        }

        private static string GetSession(string path)
        {
            var res = "unknown_session";

            var r = new Regex(@"\\\d\d\d\d\\\d\d\d\d-\d\d-\d\d\s+-\s+(?<Session>.*?)\\");
            var match = r.Match(path);
            if (match.Success)
            {
                res = match.Groups["Session"].Value;
            }

            return res;
        }

        private static object NormalizeCamera(string camera)
        {
            var res = camera;
            switch (camera)
            {
                case "533MC":
                case "2600MM":
                case "585MM":
                    res += "-Pro";
                    break;
                case "174MM":
                    res += "-Mini";
                    break;
            }

            return res;
        }

        private static string Timestamp()
        {
            return DateTime.Now.ToString("yyyy-MM-dd_HH.mm.ss");
        }

        static private void Usage()
        {
            var cmdLineArgs = Environment.GetCommandLineArgs();
            var exe = Path.GetFileName(cmdLineArgs[0]);
            Console.WriteLine("Usage: {0} [<directory> | <file>]", exe);
            Console.WriteLine("");
            Console.WriteLine("<directory> directory to process");
            Console.WriteLine("<file>      file with list of files to process");
            Console.WriteLine("");
        }
    }

    internal class SubInfo
    {
        public required string Path { get; set; }
        public required string ObjectName { get; set; }
        public required string ExposureDurationStr { get; set; }
        public required string Camera { get; set; }
        public required string ExposureEndDateTimeStr { get; set; }
        public required string Filter { get; set; }
        public required string Telescope { get; set; }
        public required string Session { get; set; }

        public bool IsBad
        {
            get
            {
                var path = this.Path.Split(System.IO.Path.DirectorySeparatorChar);

                // remove all empty path parts from end
                while ((path != null) && (path.Length >= 1) && (String.IsNullOrEmpty(path.Last())))
                    path = path.SkipLast(1).ToArray();

                if (
                    (path != null)
                    &&
                    (path.Length >= 2)
                    &&
                    (
                        (path.Last().ToUpper().Contains("_BAD".ToUpper()))
                        ||
                        ((path.SkipLast(1).Last()).ToUpper().Contains("_BAD".ToUpper()))
                    )
                )
                {
                    return true;
                }

                return false;
            }
        }

        public decimal ExposureDurationMilliseconds
        {
            get
            {
                //var d = decimal.Parse(this.ExposureDurationStr.TrimEnd('s'));
                //return Convert.ToInt32(d);

                var secondsRegex = new Regex(@"(?<seconds>\d+)s");
                var secondsMatch = secondsRegex.Match(this.ExposureDurationStr);
                if (secondsMatch.Success)
                {
                    if (int.TryParse(secondsMatch.Groups["seconds"].Value, out int res))
                        return res;
                }

                var millisecondsRegex = new Regex(@"(?<milliseconds>\d+)ms");
                var millisecondsMatch = millisecondsRegex.Match(this.ExposureDurationStr);
                if (millisecondsMatch.Success)
                {
                    if (decimal.TryParse(millisecondsMatch.Groups["milliseconds"].Value, out decimal res))
                        return res/1000;
                }

                throw new Exception("Could not parse exposure duration \"" + this.ExposureDurationStr + "\".");
            }
        }

        public DateTime ExposureStartDateTime => ExposureEndDateTime.AddMilliseconds(-1 * (double)this.ExposureDurationMilliseconds);

        public static DateTime ExtractExposureDateTime(string exposureDateTimeStr)
        {
            return
                new DateTime(
                    int.Parse(exposureDateTimeStr.Substring(0, 4)),
                    int.Parse(exposureDateTimeStr.Substring(4, 2)),
                    int.Parse(exposureDateTimeStr.Substring(6, 2)),
                    int.Parse(exposureDateTimeStr.Substring(9, 2)),
                    int.Parse(exposureDateTimeStr.Substring(11, 2)),
                    int.Parse(exposureDateTimeStr.Substring(13, 2))
                );
        }

        public DateTime ExposureEndDateTime
        {
            get
            {
                return ExtractExposureDateTime(this.ExposureEndDateTimeStr);
            }
        }

        public DateOnly NormalizedExposureDate
        {
            get
            {
                var res = new DateOnly(
                    ExposureStartDateTime.Year,
                    ExposureStartDateTime.Month,
                    ExposureStartDateTime.Day);

                // shift after midnight subs to previous day (i.e. Normalized date)
                if (ExposureStartDateTime.Hour < 12)
                    res = res.AddDays(-1);

                return res;
            }
        }

        public override int GetHashCode() => (
            NormalizedExposureDate,
            ObjectName,
            Camera,
            Filter,
            ExposureDurationStr,
            Telescope
        ).GetHashCode();
    }

    internal class ShootingKey
    {
        public required DateOnly NormalizedExposureDate { get; set; }
        public required string ObjectName { get; set; }
        public required string Camera { get; set; }
        public required string Filter { get; set; }
        public required string ExposureDurationStr { get; set; }
        public required string Telescope { get; set; }
        public required string Session { get; set; }
        public int ExposureDurationMilliseconds
        {
            get
            {
                var dur = this.ExposureDurationStr.ToLower();

                if (dur.EndsWith("ms"))
                {
                    return 
                        (int)decimal.Parse(dur
                            .TrimEnd('s')
                            .TrimEnd('m')
                        );
                }

                if (dur.EndsWith("s"))
                {
                    return
                        (int)decimal.Parse(dur
                            .TrimEnd('s')
                        ) * 1000;
                }

                throw new Exception("Unrecognized exposure time: " + dur);
            }
        }

        private static readonly string _sep = "&&&";
        public override string ToString() => String.Join(_sep,
                            /* 0 */ NormalizedExposureDate.ToString("yyyy-MM-dd"),
                            /* 1 */ ObjectName,
                            /* 2 */ Camera,
                            /* 3 */ Filter,
                            /* 4 */ ExposureDurationStr,
                            /* 5 */ Telescope,
                            /* 6 */ Session
        );
        public static ShootingKey FromString(string key)
        {
            var k = key.Split(_sep);
            return new ShootingKey()
            {
                NormalizedExposureDate = DateOnly.FromDateTime(DateTime.ParseExact(k[0], "yyyy-MM-dd", CultureInfo.InvariantCulture)),
                ObjectName = k[1],
                Camera = k[2],
                Filter = k[3],
                ExposureDurationStr= k[4],
                Telescope = k[5],
                Session = k[6],
            };
        }
    }

    internal class ShootingInfo
    {
        public int SubsCount { get; set; }
        public DateTime StartDateTime { get; set; }
        public DateTime EndDateTime { get; set; }
    }
}
