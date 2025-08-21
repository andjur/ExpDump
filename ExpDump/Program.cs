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
            var sep = "&&&";
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
                        Filter =String.IsNullOrEmpty(match.Groups["Filter"].Value) ? "unknown_filter" : match.Groups["Filter"].Value,
                    };

                    //Console.WriteLine(String.Join("; ",
                    //    //(subInfo.IsBad ? "BAD: " : "") +
                    //    subInfo.NormalizedExposureDate.ToString("yyyy-MM-dd"),
                    //    subInfo.ObjectName,
                    //    subInfo.ExposureStartDateTime.ToString("yyyy-MM-dd_HH.mm.ss"),
                    //    subInfo.ExposureEndDateTime.ToString("yyyy-MM-dd_HH.mm.ss"),
                    //    subInfo.ExposureDurationSeconds.ToString(),
                    //    subInfo.Filter
                    //));

                    if (!subInfo.IsBad)
                    {
                        // we have a "good" (i.e. not BAD) sub
                        var key = String.Join(sep,
                            /* 0 */ subInfo.NormalizedExposureDate.ToString("yyyy-MM-dd"),
                            /* 1 */ subInfo.ObjectName,
                            /* 2 */ subInfo.Camera,
                            /* 3 */ subInfo.Filter,
                            /* 4 */ subInfo.ExposureDurationSeconds.ToString()
                        );

                        // include/integrate sub into stats
                        if (res.ContainsKey(key))
                        {
                            res[key].SubsCount++;
                            if (subInfo.ExposureStartDateTime < res[key].StartDateTime)
                                res[key].StartDateTime = subInfo.ExposureStartDateTime;
                            if (subInfo.ExposureEndDateTime > res[key].EndDateTime)
                                res[key].EndDateTime = subInfo.ExposureEndDateTime;
                        }
                        else
                            res[key] = new IntegrationInfo()
                            {
                                SubsCount = 1,
                                StartDateTime = subInfo.ExposureStartDateTime,
                                EndDateTime = subInfo.ExposureEndDateTime,
                            };
                    }
                }
            }

            var sb = new StringBuilder();
            var sbSep = ";";
            sb.AppendLine(String.Join(sbSep,
                "Normalized Date",
                "Object",
                "Camera",
                "Filter",
                "Start Time",
                "End Time",
                "Total Time",
                "Subs Count",
                "Sub Duration",
                "Details"
            ));
            foreach (var key in res.Keys)
            {
                var k = key.Split(sep);
                var integrationSeconds = int.Parse(k[4]) * res[key].SubsCount;
                var integrationHours = integrationSeconds / 3600;
                var integrationMinutesFracion = (integrationSeconds - integrationHours * 3600) / 60;
                var idleTimeSeconds = (res[key].EndDateTime - res[key].StartDateTime).TotalSeconds - integrationSeconds;
                //Console.WriteLine(
                sb.AppendLine(
                    String.Join(sbSep,
                        k[0], // Nomalized Date
                        k[1], // Object
                        k[2], // Camera
                        k[3], // Filter
                        res[key].StartDateTime.ToString("H:mm"), //res[key].StartDateTime.ToString("yyyy-MM-dd H:mm"),
                        res[key].EndDateTime.ToString("H:mm"), //res[key].EndDateTime.ToString("yyyy-MM-dd H:mm"),
                                                               //"idle: "+(idleTimeSeconds/60).ToString("N1")+" minutes",
                        integrationHours.ToString() + ":" + integrationMinutesFracion.ToString("D2"),
                        res[key].SubsCount.ToString(),
                        k[4], // ExposureDurationSeconds
                        String.Join(" ",
                            k[3], // Filter
                            res[key].SubsCount.ToString() + "x" + k[4] + "s",
                            "(" + integrationHours.ToString() + ":" + integrationMinutesFracion.ToString("D2") + ")"
                        )
                    )
                );
            }

            return sb.ToString();
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

        public DateTime NormalizedExposureDate
        {
            get
            {
                var res = new DateTime(
                    ExposureStartDateTime.Year,
                    ExposureStartDateTime.Month,
                    ExposureStartDateTime.Day);

                // shift after midnight subs to previous day (i.e. Normalized date)
                if (ExposureStartDateTime.Hour < 12)
                    res = res.AddDays(-1);

                return res;
            }
        }
    }

    internal class IntegrationInfo
    {
        public int SubsCount { get; set; }
        public DateTime StartDateTime { get; set; }
        public DateTime EndDateTime { get; set; }
    }
}
