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
                    File.WriteAllText("__ExpDump_" + Timestamp() + ".csv", res);
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
            var res = new Dictionary<string, IntegrationInfo>(); // TKey is date/object/filter/duration combination; TValue is subs count
            foreach (string fileName in fileNames)
            {
                var r = new Regex(@"(?<Path>.*?)Light_(?<ObjectName>.*?)_.*?(?<ExposureDuration>.*?s)_.+?_(?<Camera>.+?)_.*(?<ExposureEndDateTime>\d{4}\d{2}\d{2}-\d{2}\d{2}\d{2}).*?_(filter_(?<Filter>.+?)_)?\d\d\d\d");
                var match = r.Match(fileName);
                if (match.Success)
                {
                    // we have a sub
                    var subInfo = new SubInfo()
                    {
                        Path = match.Groups["Path"].Value,
                        ObjectName = match.Groups["ObjectName"].Value,
                        ExposureDurationStr = match.Groups["ExposureDuration"].Value,
                        Camera = match.Groups["Camera"].Value,
                        ExposureEndDateTimeStr = match.Groups["ExposureEndDateTime"].Value,
                        Filter = String.IsNullOrEmpty(match.Groups["Filter"].Value) ? "unknown_filter" : match.Groups["Filter"].Value,
                        Telescope = DetectTelescope(match.Groups["Path"].Value),
                    };

                    if (!subInfo.IsBad)
                    {
                        // we have a "good" (i.e. not BAD) sub
                        var integrationKey = new IntegrationKey() {
                            NormalizedExposureDate = subInfo.NormalizedExposureDate,
                            ObjectName = subInfo.ObjectName,
                            Camera = subInfo.Camera,
                            Filter = subInfo.Filter,
                            ExposureDurationSeconds = subInfo.ExposureDurationSeconds,
                            Telescope = subInfo.Telescope,
                        };

                        // include/integrate sub into stats
                        var key = integrationKey.ToString();
                        if (res.ContainsKey(key))
                        {
                            var integrationInfo = res[key];
                            integrationInfo.SubsCount++;
                            if (subInfo.ExposureStartDateTime < integrationInfo.StartDateTime)
                                integrationInfo.StartDateTime = subInfo.ExposureStartDateTime;
                            if (subInfo.ExposureEndDateTime > integrationInfo.EndDateTime)
                                integrationInfo.EndDateTime = subInfo.ExposureEndDateTime;
                        }
                        else
                        {
                            res[key] = new IntegrationInfo()
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
                "Start Time",
                "End Time",
                "Total Time",
                "Subs Count",
                "Sub Duration",
                "Details"
            ));
            foreach (var key in res.Keys.OrderBy(key => key))
            {
                var k = IntegrationKey.FromString(key);
                var integrationInfo = res[key];
                var integrationSeconds = k.ExposureDurationSeconds * integrationInfo.SubsCount;
                var integrationHours = integrationSeconds / 3600;
                var integrationMinutesFracion = (integrationSeconds - integrationHours * 3600) / 60;
                var idleTimeSeconds = (integrationInfo.EndDateTime - integrationInfo.StartDateTime).TotalSeconds - integrationSeconds;
                //Console.WriteLine(
                sb.AppendLine(
                    String.Join(sbSep,
                        k.NormalizedExposureDate.ToString("yyyy-MM-dd"),
                        k.ObjectName,
                        k.Telescope,
                        NormalizeCamera(k.Camera),
                        k.Filter,
                        integrationInfo.StartDateTime.ToString("H:mm"), //integrationInfo.StartDateTime.ToString("yyyy-MM-dd H:mm"),
                        integrationInfo.EndDateTime.ToString("H:mm"), //integrationInfo.EndDateTime.ToString("yyyy-MM-dd H:mm"),
                                                                      //"idle: "+(idleTimeSeconds/60).ToString("N1")+" minutes",
                        integrationHours.ToString() + ":" + integrationMinutesFracion.ToString("D2"),
                        integrationInfo.SubsCount.ToString(),
                        k.ExposureDurationSeconds,
                        String.Join(" ",
                            k.Filter,
                            integrationInfo.SubsCount.ToString() + "x" + k.ExposureDurationSeconds + "s",
                            "(" + integrationHours.ToString() + ":" + integrationMinutesFracion.ToString("D2") + ")"
                        )
                    )
                );
            }

            return sb.ToString();
        }

        private static string DetectTelescope(string path)
        {
            var res = "unknown_telescope";

            var r = new Regex(@"(?<Telescope>C9\.25|SQA55|Samyang|AllSky)");
            var match = r.Match(path);
            if (match.Success)
            {
                res = match.Groups["Telescope"].Value;
            }

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

        public int ExposureDurationSeconds
        {
            get
            {
                //var d = decimal.Parse(this.ExposureDurationStr.TrimEnd('s'));
                //return Convert.ToInt32(d);

                var r = new Regex(@"(?<seconds>\d+)");
                var match = r.Match(this.ExposureDurationStr);
                if (match.Success)
                {
                    if (int.TryParse(match.Groups["seconds"].Value, out int res))
                        return res;
                }

                return 0;
            }
        }

        public DateTime ExposureStartDateTime
        {
            get
            {
                return ExposureEndDateTime.AddSeconds(-1 * this.ExposureDurationSeconds);
            }
        }

        public DateTime ExposureEndDateTime
        {
            get
            {
                return new DateTime(
                    int.Parse(this.ExposureEndDateTimeStr.Substring(0, 4)),
                    int.Parse(this.ExposureEndDateTimeStr.Substring(4, 2)),
                    int.Parse(this.ExposureEndDateTimeStr.Substring(6, 2)),
                    int.Parse(this.ExposureEndDateTimeStr.Substring(9, 2)),
                    int.Parse(this.ExposureEndDateTimeStr.Substring(11, 2)),
                    int.Parse(this.ExposureEndDateTimeStr.Substring(13, 2))
                );
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
            ExposureDurationSeconds,
            Telescope
        ).GetHashCode();
    }

    internal class IntegrationKey
    {
        public required DateOnly NormalizedExposureDate { get; set; }
        public required string ObjectName { get; set; }
        public required string Camera { get; set; }
        public required string Filter { get; set; }
        public required int ExposureDurationSeconds { get; set; }
        public required string Telescope { get; set; }

        private static readonly string _sep = "&&&";
        public override string ToString() => String.Join(_sep,
                            /* 0 */ NormalizedExposureDate.ToString("yyyy-MM-dd"),
                            /* 1 */ ObjectName,
                            /* 2 */ Camera,
                            /* 3 */ Filter,
                            /* 4 */ ExposureDurationSeconds.ToString(),
                            /* 5 */ Telescope
        );
        public static IntegrationKey FromString(string key)
        {
            var k = key.Split(_sep);
            return new IntegrationKey()
            {
                NormalizedExposureDate = DateOnly.FromDateTime(DateTime.ParseExact(k[0], "yyyy-MM-dd", CultureInfo.InvariantCulture)),
                ObjectName = k[1],
                Camera = k[2],
                Filter = k[3],
                ExposureDurationSeconds = int.Parse(k[4]),
                Telescope = k[5]
            };
        }
    }

    internal class IntegrationInfo
    {
        public int SubsCount { get; set; }
        public DateTime StartDateTime { get; set; }
        public DateTime EndDateTime { get; set; }
    }
}
