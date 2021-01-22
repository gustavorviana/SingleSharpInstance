using SingleSharpInstance;
using SingleSharpInstance.Events;
using System;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

namespace ConsoleApp1
{
    class Program
    {
        static bool Running = true;
        static void Main(string[] args)
        {
            try
            {
                var single = SingleSharp.From<Program>(null, CreatePipeSecurity());
                Console.Title = single.IsMainInstance ? "Main instance" : "Another instance";
                single.OnReceiveActivation += OnReceiveActivation;

                while (Running)
                {
                    var res = Console.ReadLine();
                    if (res == "")
                        break;

                    Console.CursorTop -= 1;
                    Console.WriteLine("Sended: " + res);
                    single.SendActivation(Environment.UserName, res);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                Console.ReadLine();
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416", Justification = "<Pendente>")]
        static PipeSecurity CreatePipeSecurity()
        {
            PipeSecurity pipeSecurity = new PipeSecurity();

            var id = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);

            // Allow Everyone read and write access to the pipe. 
            pipeSecurity.SetAccessRule(new PipeAccessRule(id, PipeAccessRights.ReadWrite, AccessControlType.Allow));

            return pipeSecurity;
        }

        private static void OnReceiveActivation(object sender, ActivationEventArgs e)
        {
            var owner = e.Args[0];
            var cmd = e.Args[1];

            Console.WriteLine($"[{owner.ToUpper()}] {cmd}");

            if (cmd == "exit" && sender is SingleSharp single)
            {
                Running = false;
                single.Shutdown();
                single.Dispose();
            }
        }
    }
}
