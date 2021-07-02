using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GZipTest
{
    class Program
    {

        static int Main(string[] args)
        {
            CmdArgs cmdArgs = new CmdArgs(args);
            if (!cmdArgs.IsValid)
            {
                Console.WriteLine(cmdArgs.GetExceptionMessage());
                return 1;
            }

            IGZipService gZipService = new GZipService();

            gZipService.ExceptionThrowed += GZipService_ExceptionThrowed;
            int result;
            try
            {
                if (cmdArgs.Command == CmdArgs.CommandTypes.Compress)
                    result = gZipService.Compress(cmdArgs.SourceFile, cmdArgs.DestinationFile);
                else
                    result = gZipService.Decompress(cmdArgs.SourceFile, cmdArgs.DestinationFile);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                result = 1;
            }

            Console.WriteLine($"Rresult = {result}");

#if DEBUG
            Console.ReadLine();
#endif
            return result;
        }

        private static void GZipService_ExceptionThrowed(Exception ex)
        {
            Console.WriteLine($"Exception - {ex.Message}");
        }

    }
}
