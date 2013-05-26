using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

namespace Weathermen
{
    //Problem: cannot easily use serialization alongside the flat file-based persistence. See no good way to do
    //if(canReadXMLFile) {do so} else{readFlatFile} within the program.
    //OTOH, can do that in the "driver"; if (existsXMLFile){readFile} else {new RainMan(location)}
    //OTGH, how does the "driver" know what XMLFile is called?
    //Finally, we don't really need to serialize RainMan; everything is derived either from records or location.
    public class RainMan
    {
        //Making RainMan into a class provides ability to store data on multiple locations;
        //Map.add("location", new RainMan(location))
        //Problem: Other functionality is needed by things other than RainMan internally; length_month and month_names,
        //f'rinstance. Can either toss them out into libraries, or make them static methods. Will do latter here.

        //Primary record storage; used for persistence, and also to demonstrate/test the use of serialization for persistence.
        private string XML_FILE;
        //Secondary record storage file. Stores data in CSV format. See read_records for the format.
        //Used for redundancy, in case XML_FILE is corrupted/nonexistent, and for dumping data to other systems.
        private string RECORD_FILE;
        //Human readable data output; data by year and month, with totals for year and month.
        //If you can understand it, dump_to_file can provide the format; else, just eyeball the file.
        private string OUTPUT_FILE;

        /* Main record-keeping data structure, we use ints for the keys because they save space over
         * strings, though it does lead to a need for String<=>int conversions. The final payload is
         * an array of doubles because it's the natural type for storing inches of rain. Additionally,
         * it permits interoperability; we can use other people's CSV files for data, without excessive
         * conversion and translation. It's also the only thing serialized; every other field is either
         * static or derived from the location.
         * records[year][month][day] = datum;
         */
        private Dictionary<int, Dictionary<int, double[]>> records = new Dictionary<int, Dictionary<int, double[]>>();
        public Dictionary<int, Dictionary<int, double[]>> Records
        {
            get
            {
                return records;
            }
        }

        //Would rather use an indexer for this, but multidimensional indexers are not allowed.
        public void setRecord(int year, int month, int day, double datum)
        {
            //We'll need both of these checks if we need to enter something for the first rain of the year,
            //and we definitely need the second for the first rain of a given month.
            if (!records.ContainsKey(year))
                records.Add(year, new Dictionary<int, double[]>());
            if (records[year].ContainsKey(month))
                records[year].Add(month, new double[length_month(month, year)]);
            //Error catching
            if (day < records[year][month].Length && day >= 0)
                records[year][month][day] = datum;
        }

        public RainMan(string location)
        {
            //Adds extensibility; we can use multiple instances of RainMan to store data on multiple locations.
            //Of course, this then raises the problem of knowing, at runtime, which locations we have data on.
            //Meh. It'll take cunning, but that can be done later, though I have ideas already.
            location = location.ToUpper(); //For consistency's sake.
            XML_FILE = location + ".rm";
            RECORD_FILE = location + "-raindata.txt";
            OUTPUT_FILE = location + "-summary.txt";
            readRecords();
        }

        ~RainMan()
        {
            writeRecords();
            //TODO dump_to_file();
        }

        /// <summary>
        /// Reads in data from file storage.
        /// </summary>
        /// <remarks>The data is stored on a month by line basis. First two tokens are year and month,
        /// everything after that is data by day. Data is stored as doubles, for ease of use and for
        /// convenience in transfer to other systems.</remarks>
        private void readRecords()
        {
            try
            {
                if (File.Exists(XML_FILE))
                {
                    Stream inStream = File.OpenRead(XML_FILE);
                    BinaryFormatter deserializer = new BinaryFormatter();
                    records = (Dictionary<int, Dictionary<int, double[]>>)deserializer.Deserialize(inStream);
                    inStream.Close();
                }
                else
                {
                    using (StreamReader record_reader = File.OpenText(RECORD_FILE))
                    {
                        String input;
                        while ((input = record_reader.ReadLine()) != null)
                        {
                            parse_data(input);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Trace.Fail("Error: Could not read from file." + e);
            }
        }//End readRecords

        /// <summary>
        /// Converts one line of data into an entry for records. Used exclusively internally, should never
        /// be called from outside.
        /// </summary>
        /// <param name="input">A single line of data, in the format YYYY MM d1 d2 d3...dK</param>
        private void parse_data(string input)
        {
            //Can read both sensible CSV and my crazy old space-delimited shit.
            String[] tokens = input.Split(',', ' ');
            int year = Convert.ToInt16(tokens[0]);
            int month = Convert.ToInt16(tokens[1]);
            //Coolest thing ever, Trace.Assert.
            Trace.Assert((month < 1 || month > MONTH_NAMES.Length), "Error: Month " + month + " outside acceptable range");
            Trace.Assert((tokens.Length == length_month(month, year) + 2), "Error: Wrong number of days in month " + year + " " + month);

            double[] data = new double[length_month(month, year)];
            for (int i = 2; i < tokens.Length; i++)
            {
                //Regrettable kludge; first two tokens are year and month, everything after
                //that is data. Can't take slices, can't say foreach, just have to map the two
                //together like this. :( 
                data[i - 2] = Convert.ToDouble(tokens[i]);
            }

            if (!records.ContainsKey(year))
                records.Add(year, new Dictionary<int, double[]>());
            records[year].Add(month, data);
        }//End parse_data

        /// <summary>
        /// Writes data out to flat-file, in much the same format as it was read in.
        /// </summary>
        /// <remarks>Must find some good way to back everything up. Everything is written out in CSV form, for consistency's sake.</remarks>
        private void writeRecords()
        {
            //Creating backup file.
            try
            {
                string BACKUP = RECORD_FILE + ".bak";
                if (File.Exists(BACKUP))
                    File.Delete(BACKUP);
                File.Copy(RECORD_FILE, BACKUP);
            }
            catch (IOException e)
            {
                Trace.Fail("Error: Could not create backup file:" + e);
            }

            //Writing out data.
            try
            {
                //serialize records
                Stream outStream = File.Create(XML_FILE);
                BinaryFormatter serializer = new BinaryFormatter();
                serializer.Serialize(outStream, records);
                outStream.Close();

                //Write CSV file.
                using (StreamWriter output = new StreamWriter(RECORD_FILE))
                {
                    foreach (int year in records.Keys)
                    {
                        foreach (int month in records[year].Keys)
                        {
                            output.Write("{0},{1}", year, month);
                            foreach (var datum in records[year][month])
                            {
                                //We need to go deeper!
                                //foreach (bit in datum)
                                output.Write(",{0}", datum);
                            }
                            output.WriteLine();
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Trace.Fail("Error: Failed while writing records" + e);
            }
        }//End write_records

        //Sadly, these next two cannot be replaced straightaway with DateTime.
        //Would that were so.
        //Used for human-readability; easier to grok "FEB 2011" than "02 2011"
        public static String[] MONTH_NAMES = {
        "JAN",
        "FEB",
        "MAR",
        "APR",
        "MAY",
        "JUN",
        "JUL",
        "AUG",
        "SEP",
        "OCT",
        "NOV",
        "DEC"};

        public static int length_month(int month, int year)
        {
            switch (month)
            {
                case 1:
                case 3:
                case 5:
                case 7:
                case 8:
                case 10:
                case 12:
                    return 31;

                case 2:
                    int length = DateTime.IsLeapYear(year) ? 29 : 28;
                    return length;
                case 4:
                case 6:
                case 9:
                case 11:
                    return 30;

                default:
                    Trace.Fail("Error: Month outside permitted range.");
                    return -1;
                //C# will not permit one to fall-through from one case to another.
                //Never mind that after Env.Exit() there will be no cases!
            }
        }//End length_month

    }//End RainMan
}//End namespace
