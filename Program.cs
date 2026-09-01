using System;
using System.Threading.Tasks;
using WolfLive.Api;
using WolfLive.Api.Commands;

namespace CSharpConsoleApp
{
    public class Program
    {
        private static IWolfClient? _client;

        public static async Task Main(string[] args)
        {
            string email =
                Environment.GetEnvironmentVariable("WOLF_EMAIL") ?? "";

            string password =
                Environment.GetEnvironmentVariable("WOLF_PASSWORD") ?? "";

            _client = new WolfClient()
                .SetupCommands()
                .WithCommandSet(c =>
                {
                    c.AddCommands<TestCommands>()
                     .WithPrefix("!");
                })
                .WithSerilog()
                .Done();

            _client.OnConnected += (_) =>
                Console.WriteLine("WOLF BOT CONNECTED!");

            var result = await _client.Login(email, password);

            Console.WriteLine(
                result
                    ? "LOGIN SUCCESS!"
                    : "LOGIN FAILED!"
            );

            await Task.Delay(-1);
        }
    }

    public class TestCommands : WolfContext
    {
        [Command("test")]
        public async Task Test(string message)
        {
            await this.Reply(
                "وصلتني الرسالة: " + message
            );
        }
    }
}
