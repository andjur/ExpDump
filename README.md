# ExpDump
Command line utility for scanning exposure files (subs/subexposures) acquired during astro imaging sessions and dumping extracted info into a .csv file.

Assuming exposures are stored as .fits files in specific folder structure containing short sessions descriptions containg info about imaged object and used equipment. This info (from folder and file names/file system paths) is extracted, normalized and exported to .csv file that can be imported into Excel and used as data source for Pivot table/chart that allows digging in subs in desired way e.g. by date, integration time, object, telescope, camera, filter and various combinations.
