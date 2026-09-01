using System;
using System.Threading.Tasks;
using WolfLive.Api;
using WolfLive.Api.Models;

namespace MazajBot
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

            if (string.IsNullOrWhiteSpace(email) ||
                string.IsNullOrWhiteSpace(password))
            {
                Console.WriteLine("WOLF_EMAIL أو WOLF_PASSWORD غير موجود.");
                return;
            }

            _client = new WolfClient();

            _client.OnConnected += client =>
            {
                Console.WriteLine("WOLF BOT CONNECTED!");
            };

            _client.OnDisconnected += (client, error) =>
            {
                Console.WriteLine("WOLF BOT DISCONNECTED: " + error);
            };

            _client.OnConnectionError += (client, error) =>
            {
                Console.WriteLine("WOLF CONNECTION ERROR: " + error);
            };

            // استقبال جميع رسائل ولف
            _client.Messaging.OnMessage += async (client, message) =>
            {
                try
                {
                    Console.WriteLine(
                        $"MESSAGE | User: {message.UserId} | " +
                        $"Group: {message.GroupId} | " +
                        $"Text: {message.Content}"
                    );

                    // نتعامل فقط مع الأرقام
                    string text = message.Content?.Trim() ?? "";

                    if (!int.TryParse(text, out int number))
                        return;

                    // رد على نفس المكان الذي وصلت منه الرسالة
                    string response =
                        $"وصلني الرقم: {number}";

                    await client.Reply(message, response);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("MESSAGE ERROR: " + ex);
                }
            };

            Console.WriteLine("جاري تسجيل الدخول...");

            bool loggedIn = await _client.Login(email, password);

            Console.WriteLine(
                loggedIn
                    ? "LOGIN SUCCESS!"
                    : "LOGIN FAILED!"
            );

            if (!loggedIn)
                return;

            Console.WriteLine("البوت جاهز ويستقبل الأرقام.");

            // إبقاء البوت شغال
            await Task.Delay(-1);
        }
    }
}
