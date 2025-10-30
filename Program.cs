using System.Globalization;
using System.Numerics;
using System.Text;
using System.Xml.Linq;
using System.Diagnostics;

namespace LoftANM
{
    class Program
    {
        //public static ANMDecoder anm = new();

        static void PrintHelp()
        {
            Console.WriteLine("Ravenloft: Strahd's Possesions ANM Decoder v1.0");
            Console.WriteLine("===============================================");
            Console.WriteLine("");
            Console.WriteLine("Usage:");
            Console.WriteLine("");
            Console.WriteLine("  loftanm <option> <filename>");
            Console.WriteLine("");
            Console.WriteLine("Options:");
            Console.WriteLine("  -f/-F: Decode Full .ANM file (each frame an image) saving the frames as .TGA files.");
            Console.WriteLine("         C:\\loftanm -f CINE00.ANM");
            Console.WriteLine("  -u/-U: Decode Unique .ANM file (only frames with image) saving the frames as .TGA files.");
            Console.WriteLine("         C:\\loftanm -u CINE00.ANM");
            Console.WriteLine("  -d/-D: Dump binary data of .ANM file in text file.");
            Console.WriteLine("         C:\\loftanm -d CINE00.ANM");
            Console.WriteLine("  -i/-I: Import .TGA file(s) whose filenames are in IMPORT.TXT ANSI text file");
            Console.WriteLine("         into XXXXXX.ANM animation file. IMPORT.TXT must exist.");
            Console.WriteLine("         The new animation file will be called as XXXXXX_NEW.ANM.");
            Console.WriteLine("         C:\\loftanm -i CINE00.ANM");
            Console.WriteLine("");
            Console.WriteLine("         Sample filenames in IMPORT.TXT file (mandatory frame/chunk):");
            Console.WriteLine("         CINE00_0346_0000.TGA");
            Console.WriteLine("         CINE00_0384_0000.TGA");
            Console.WriteLine("");
        }


        static bool ChecksPassed(string filename)
        {
            bool bPassed = true;

            if (File.Exists(filename))
            {
                if (!(Path.GetExtension(filename).Equals(".ANM", StringComparison.OrdinalIgnoreCase)))
                {
                    Console.WriteLine("The file must have .ANM extension.\n");
                    bPassed = false;
                }
            }
            else
            {
                Console.WriteLine("The file " + filename + " does not exist.\n");
                bPassed = false;
            }

            return bPassed;
        }


        static void Main(string[] args)
        {

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            if (args.Length == 2)
            {
                switch (args[0].ToLower())
                {
                    case "-f":
                    case "-u":

                        if (ChecksPassed(args[1]))
                        {
                            ANMDecoder.LoadANM(args[1]);

                            if (ANMDecoder.GetFrames() > 0)
                                if (args[0].ToLower() == "-f")
                                    ANMDecoder.SaveTGAFrames(Path.GetFileNameWithoutExtension(args[1]).ToUpper(), true);
                                else
                                    ANMDecoder.SaveTGAFrames(Path.GetFileNameWithoutExtension(args[1].ToUpper()), false);
                            else
                                {
                                    Console.WriteLine("You have not read any frame from the file.\n");
                                    PrintHelp();
                                }
                        }
                        else PrintHelp();

                        break;

                    case "-d":
                        if (ChecksPassed(args[1]))
                        {
                            ANMDecoder.LoadANM(args[1]);

                            if (ANMDecoder.GetFrames() > 0)
                                ANMDecoder.DumpBinaryData(Path.GetFileNameWithoutExtension(args[1]).ToUpper());
                            else
                            {
                                Console.WriteLine("You have not read any frame from the file.\n");
                                PrintHelp();
                            }
                        }
                        else PrintHelp();

                        break;

                    case "-i":
                        if (ChecksPassed(args[1]))
                        {
                            if (File.Exists("IMPORT.TXT"))
                            {
                                string[] importFile = File.ReadAllLines("IMPORT.TXT");

                                if (importFile.Length > 0)
                                {
                                    ANMDecoder.LoadANM(args[1]);
                                    ANMDecoder.ImportTGA2ANM(
                                        Path.GetFileNameWithoutExtension(args[1]).ToUpper() + 
                                        "_NEW.ANM", importFile);
                                }
                                else
                                {
                                    Console.WriteLine("The IMPORT.TXT file must have some valid filename for import.\n");
                                    PrintHelp();
                                }                                
                            }
                            else
                            {
                                Console.WriteLine("There is not any IMPORT.TXT file for import the images.\n");
                                PrintHelp();
                            }
                        }

                        break;

                    default:
                        PrintHelp();
                        break;
                }
            }
            else
            {
                PrintHelp();
                return;
            }
        }
    }
}
