using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WolfLive.Api;
using WolfLive.Api.Models;

namespace MazajBot
{
    public class Program
    {
        private static IWolfClient? _client;
        private static MazajGame? _game;

        public static async Task Main(string[] args)
        {
            Console.WriteLine("🎭🔥 Mazaj Bot Starting...");

            string email = Environment.GetEnvironmentVariable("WOLF_EMAIL") ?? "";
            string password = Environment.GetEnvironmentVariable("WOLF_PASSWORD") ?? "";

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                Console.WriteLine("❌ WOLF_EMAIL أو WOLF_PASSWORD غير موجودة.");
                return;
            }

            try
            {
                _client = new WolfClient();

                _client.Messaging.OnMessage += async (client, message) =>
                {
                    try
                    {
                        if (message == null)
                            return;

                        Console.WriteLine(
                            $"📩 Message | User: {message.UserId} | Group: {message.GroupId} | Content: {message.Content}"
                        );

                        string content = (message.Content ?? "").Trim();

                        if (string.IsNullOrWhiteSpace(content))
                            return;

                        // اختيار رقم البطاقة بدون أمر
                        if (TryParseNumber(content, out int cardNumber))
                        {
                            await ChooseCard(client, message, cardNumber);
                            return;
                        }

                        // أوامر مزاج
                        if (content.StartsWith("!مزاج", StringComparison.OrdinalIgnoreCase))
                        {
                            string commandText = content.Substring(5).Trim();

                            await HandleCommand(client, message, commandText);
                            return;
                        }

                        // دعم !mazaj
                        if (content.StartsWith("!mazaj", StringComparison.OrdinalIgnoreCase))
                        {
                            string commandText = content.Substring(6).Trim();

                            await HandleCommand(client, message, commandText);
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ Message Error: {ex}");
                    }
                };

                await _client.Connect();

                Console.WriteLine("✅ Connected to Wolf.");

                await _client.Messaging.Initialize();

                Console.WriteLine("✅ Messaging initialized.");

                await Task.Delay(Timeout.Infinite);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Bot Error: {ex}");
            }
        }

        private static async Task HandleCommand(
            IWolfClient client,
            Message message,
            string commandText)
        {
            string[] parts = commandText
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 0)
            {
                await SendHelp(client, message);
                return;
            }

            string command = parts[0].ToLowerInvariant();

            switch (command)
            {
                case "جديد":
                    await NewGame(client, message, parts);
                    break;

                case "انضم":
                    await JoinTeam(client, message, parts);
                    break;

                case "تغيير":
                    await ChangeTeam(client, message, parts);
                    break;

                case "لاعبين":
                    await ShowPlayers(client, message);
                    break;

                case "بدء":
                    await StartGame(client, message);
                    break;

                case "اختار":
                    if (parts.Length >= 2 &&
                        TryParseNumber(parts[1], out int selectedNumber))
                    {
                        await ChooseCard(client, message, selectedNumber);
                    }
                    else
                    {
                        await message.Reply(
                            client,
                            "❌ استخدم:\n!مزاج اختار <رقم>"
                        );
                    }

                    break;

                case "بطاقات":
                    await ShowCards(client, message);
                    break;

                case "انهاء":
                    await EndGame(client, message);
                    break;

                case "مساعدة":
                case "help":
                    await SendHelp(client, message);
                    break;

                default:
                    await message.Reply(
                        client,
                        "❌ الأمر غير معروف.\nاكتب !مزاج مساعدة"
                    );
                    break;
            }
        }

        private static async Task NewGame(
            IWolfClient client,
            Message message,
            string[] parts)
        {
            if (parts.Length < 3)
            {
                await message.Reply(
                    client,
                    "❌ الاستخدام الصحيح:\n!مزاج جديد <النقاط لكل بطاقة> <عدد الفرق>\n\nمثال:\n!مزاج جديد 2 4"
                );

                return;
            }

            if (!int.TryParse(parts[1], out int points))
            {
                await message.Reply(
                    client,
                    "❌ عدد النقاط غير صحيح."
                );

                return;
            }

            if (!int.TryParse(parts[2], out int teamCount))
            {
                await message.Reply(
                    client,
                    "❌ عدد الفرق غير صحيح."
                );

                return;
            }

            if (points <= 0)
            {
                await message.Reply(
                    client,
                    "❌ النقاط يجب أن تكون أكبر من صفر."
                );

                return;
            }

            if (teamCount < 2 || teamCount > 4)
            {
                await message.Reply(
                    client,
                    "❌ عدد الفرق يجب أن يكون من 2 إلى 4."
                );

                return;
            }

            _game = new MazajGame(points, teamCount);

            _game.GroupId = message.GroupId ?? "";

            if (!string.IsNullOrWhiteSpace(_game.GroupId))
            {
                try
                {
                    await client.Messaging.GroupMessageSubscribe(_game.GroupId);

                    Console.WriteLine(
                        $"✅ Subscribed to group: {_game.GroupId}"
                    );
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"⚠️ Group subscribe error: {ex.Message}"
                    );
                }
            }

            await message.Reply(
                client,
                $"🎭🔥 تم إنشاء لعبة مزاج بنجاح!\n\n" +
                $"💰 النقاط لكل بطاقة: {points}\n" +
                $"👥 عدد الفرق: {teamCount}\n" +
                $"🎴 عدد البطاقات: 65\n\n" +
                $"🟢 الخطوة التالية:\n" +
                $"!مزاج انضم <الفريق>\n\n" +
                $"الألوان المتاحة:\n" +
                $"🔴 احمر\n" +
                $"🔵 ازرق\n" +
                $"🟡 اصفر\n" +
                $"🟣 بنفسجي\n\n" +
                $"بعد اكتمال اللاعبين اكتب:\n" +
                $"!مزاج بدء"
            );
        }

        private static async Task JoinTeam(
            IWolfClient client,
            Message message,
            string[] parts)
        {
            if (_game == null)
            {
                await message.Reply(
                    client,
                    "❌ لا توجد لعبة حالياً.\nاكتب !مزاج جديد لإنشاء لعبة."
                );

                return;
            }

            if (_game.Started)
            {
                await message.Reply(
                    client,
                    "❌ اللعبة بدأت بالفعل."
                );

                return;
            }

            if (parts.Length < 2)
            {
                await message.Reply(
                    client,
                    "❌ الاستخدام:\n!مزاج انضم <احمر|ازرق|اصفر|بنفسجي>"
                );

                return;
            }

            string teamName = NormalizeTeam(parts[1]);

            if (string.IsNullOrWhiteSpace(teamName))
            {
                await message.Reply(
                    client,
                    "❌ الفريق غير صحيح.\n\nالمتاح:\n🔴 احمر\n🔵 ازرق\n🟡 اصفر\n🟣 بنفسجي"
                );

                return;
            }

            Team? team = _game.Teams.FirstOrDefault(
                t => t.Name == teamName
            );

            if (team == null)
            {
                await message.Reply(
                    client,
                    "❌ هذا الفريق غير موجود في هذه اللعبة."
                );

                return;
            }

            if (_game.GetTeamByPlayer(message.UserId) != null)
            {
                await message.Reply(
                    client,
                    "❌ أنت منضم إلى فريق بالفعل."
                );

                return;
            }

            if (team.Players.ContainsKey(message.UserId))
            {
                await message.Reply(
                    client,
                    "❌ أنت موجود في هذا الفريق بالفعل."
                );

                return;
            }

            string
