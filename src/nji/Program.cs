namespace nji
{
    using System;
    using System.Linq;
    using System.Threading;

    class Program
    {
        static void Main(string[] args)
        {
            var nji = new NjiApi();
            nji.CleanTempDir();

            var cst = new CancellationTokenSource();
            Console.CancelKeyPress += (o, e) =>
            {
                // todo: support cancellation
                // cst.Cancel();
            };

            if (args.Length < 1)
                Usage();

            try
            {
                string subcommand = args[0];
                if (subcommand == "install")
                    nji.InstallAsync(args.Skip(1), cst.Token).Wait();
                else if (subcommand == "update")
                    nji.UpdateAsync(cst.Token).Wait();
                else
                    Usage();
            }
            finally
            {
                cst.Dispose();
            }

            nji.CleanTempDir();
        }

        private static void Usage()
        {
            Console.WriteLine(@"Usage:
 nji install [<pkg>] - Install package(s), and it's/there dependencies
 nji update          - Checks for different version of packages in online repository, and updates as needed

Example:
  nji install
  nji install easy
  nji install express socket.io mongolian underscore
  nji install http://registry.npmjs.org/connect/-/connect-1.7.1.tgz
  nji update");

            Environment.Exit(1);
        }
    }
}