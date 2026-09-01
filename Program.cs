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
            string email =
                Environment.GetEnvironmentVariable("WOLF_EMAIL") ?? "";

            string password =
                Environment.GetEnvironmentVariable("WOLF_PASSWORD") ?? "";

            if (string.IsNullOrWhiteSpace(email) ||
                string.IsNullOrWhiteSpace(password))
            {
                Console.WriteLine(
                    "❌ WOLF_EMAIL أو WOLF_PASSWORD غير موجود."
                );

                return;
            }

            Console.WriteLine("🚀 تشغيل Mazaj Bot...");

            _client = new WolfClient();

            // =====================================================
            // استقبال الرسائل
            // =====================================================

            _client.Messaging.OnMessage += async (client, message) =>
            {
                try
                {
                    string text =
                        message.Content?.Trim() ?? "";

                    Console.WriteLine(
                        $"📩 رسالة: {text} | Group: {message.GroupId} | User: {message.UserId}"
                    );

                    // =================================================
                    // الأرقام المباشرة
                    // =================================================

                    if (TryParseNumber(text, out int directNumber))
                    {
                        // خارج اللعبة = تجاهل بصمت
                        if (_game != null &&
                            _game.Started)
                        {
                            await ChooseCard(
                                client,
                                message,
                                directNumber,
                                true
                            );
                        }

                        return;
                    }

                    // =================================================
                    // أوامر مزاج
                    // =================================================

                    if (!text.StartsWith(
                            "!مزاج",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }

                    string command =
                        text.Length > 5
                            ? text.Substring(5).Trim()
                            : "";

                    await HandleCommand(
                        client,
                        message,
                        command
                    );
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        "❌ COMMAND ERROR: " +
                        ex
                    );
                }
            };

            // =====================================================
            // الاتصال
            // =====================================================

            try
            {
                await _client.Connect();

                Console.WriteLine(
                    "✅ تم الاتصال بـ Wolf"
                );

                await _client.Messaging.Initialize();

                Console.WriteLine(
                    "✅ تم تفعيل استقبال الرسائل"
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    "❌ CONNECTION ERROR:"
                );

                Console.WriteLine(ex);

                return;
            }

            // =====================================================
            // إبقاء البوت يعمل
            // =====================================================

            await Task.Delay(
                Timeout.Infinite
            );
        }

        // =========================================================
        // الأوامر
        // =========================================================

        private static async Task HandleCommand(
            IWolfClient client,
            Message message,
            string command)
        {
            string[] parts =
                command.Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries
                );

            if (parts.Length == 0)
            {
                await SendHelp(
                    client,
                    message
                );

                return;
            }

            string action =
                parts[0].ToLowerInvariant();

            switch (action)
            {
                case "جديد":
                    await NewGame(
                        client,
                        message,
                        parts
                    );
                    break;

                case "انضم":
                    await JoinTeam(
                        client,
                        message,
                        parts
                    );
                    break;

                case "تغيير":
                    await ChangeTeam(
                        client,
                        message,
                        parts
                    );
                    break;

                case "لاعبين":
                    await ShowPlayers(
                        client,
                        message
                    );
                    break;

                case "بدء":
                    await StartGame(
                        client,
                        message
                    );
                    break;

                case "اختار":

                    if (parts.Length >= 2 &&
                        TryParseNumber(
                            parts[1],
                            out int number))
                    {
                        await ChooseCard(
                            client,
                            message,
                            number,
                            false
                        );
                    }
                    else
                    {
                        await client.Reply(
                            message,
                            "❌ استخدم:\n!مزاج اختار <رقم>"
                        );
                    }

                    break;

                case "بطاقات":
                    await ShowCards(
                        client,
                        message
                    );
                    break;

                case "انهاء":
                    await EndGame(
                        client,
                        message
                    );
                    break;

                case "مساعدة":
                case "help":
                    await SendHelp(
                        client,
                        message
                    );
                    break;

                default:

                    await client.Reply(
                        message,
                        "❌ أمر غير معروف.\n" +
                        "اكتب !مزاج مساعدة"
                    );

                    break;
            }
        }

        // =========================================================
        // إنشاء لعبة
        // =========================================================

        private static async Task NewGame(
            IWolfClient client,
            Message message,
            string[] parts)
        {
            if (_game != null)
            {
                await client.Reply(
                    message,
                    "⚠️ توجد لعبة حالياً.\n" +
                    "استخدم !مزاج انهاء أولاً."
                );

                return;
            }

            if (parts.Length < 3 ||
                !int.TryParse(
                    parts[1],
                    out int points) ||
                !int.TryParse(
                    parts[2],
                    out int teamCount))
            {
                await client.Reply(
                    message,
                    "❌ الاستخدام الصحيح:\n" +
                    "!مزاج جديد <النقاط لكل بطاقة> <عدد الفرق>\n\n" +
                    "مثال:\n" +
                    "!مزاج جديد 50 2"
                );

                return;
            }

            if (points <= 0)
            {
                await client.Reply(
                    message,
                    "❌ يجب أن تكون نقاط البطاقة أكبر من صفر."
                );

                return;
            }

            if (teamCount < 2 ||
                teamCount > 4)
            {
                await client.Reply(
                    message,
                    "❌ عدد الفرق يجب أن يكون من 2 إلى 4."
                );

                return;
            }

            _game =
                new MazajGame(
                    points,
                    teamCount
                );

            _game.GroupId =
                message.GroupId ?? "";

            // =====================================================
            // الاشتراك برسائل المجموعة
            // =====================================================

            if (!string.IsNullOrWhiteSpace(
                    _game.GroupId))
            {
                try
                {
                    await client.Messaging
                        .GroupMessageSubscribe(
                            _game.GroupId
                        );

                    Console.WriteLine(
                        $"✅ تم الاشتراك بالمجموعة: {_game.GroupId}"
                    );
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        "⚠️ GROUP SUBSCRIBE ERROR: " +
                        ex.Message
                    );
                }
            }

            await client.Reply(
                message,
                "🎭🔥 تم إنشاء لعبة مزاج بنجاح!\n\n" +
                $"💰 نقاط البطاقة: {points}\n" +
                $"👥 عدد الفرق: {teamCount}\n" +
                "🎴 عدد البطاقات: 65\n\n" +
                "📌 للانضمام:\n" +
                "!مزاج انضم <احمر|ازرق|اصفر|بنفسجي>\n\n" +
                "📌 بعد اكتمال اللاعبين:\n" +
                "!مزاج بدء"
            );
        }

        // =========================================================
        // الانضمام
        // =========================================================

        private static async Task JoinTeam(
            IWolfClient client,
            Message message,
            string[] parts)
        {
            if (_game == null)
            {
                await client.Reply(
                    message,
                    "❌ لا توجد لعبة حالياً."
                );

                return;
            }

            if (_game.Started)
            {
                await client.Reply(
                    message,
                    "❌ اللعبة بدأت بالفعل."
                );

                return;
            }

            if (parts.Length < 2)
            {
                await client.Reply(
                    message,
                    "❌ الاستخدام:\n" +
                    "!مزاج انضم <احمر|ازرق|اصفر|بنفسجي>"
                );

                return;
            }

            string teamName =
                NormalizeTeam(parts[1]);

            Team? team =
                _game.Teams.FirstOrDefault(
                    x => x.Name == teamName
                );

            if (team == null)
            {
                await client.Reply(
                    message,
                    "❌ الفريق غير موجود أو غير متاح."
                );

                return;
            }

            string userId =
                message.UserId;

            string nickname =
                await GetNickname(
                    client,
                    userId
                );

            foreach (Team existingTeam in _game.Teams)
            {
                if (existingTeam.Players
                    .ContainsKey(userId))
                {
                    await client.Reply(
                        message,
                        $"⚠️ أنت منضم مسبقاً إلى " +
                        $"{existingTeam.Emoji} " +
                        $"{existingTeam.Name}."
                    );

                    return;
                }
            }

            team.Players[userId] =
                nickname;

            await client.Reply(
                message,
                $"✅ تم انضمامك إلى " +
                $"{team.Emoji} {team.Name}\n" +
                $"👤 اللاعب: {nickname}"
            );
        }

        // =========================================================
        // تغيير الفريق
        // =========================================================

        private static async Task ChangeTeam(
            IWolfClient client,
            Message message,
            string[] parts)
        {
            if (_game == null)
            {
                await client.Reply(
                    message,
                    "❌ لا توجد لعبة حالياً."
                );

                return;
            }

            if (_game.Started)
            {
                await client.Reply(
                    message,
                    "❌ لا يمكن تغيير الفريق بعد بدء اللعبة."
                );

                return;
            }

            if (parts.Length < 2)
            {
                await client.Reply(
                    message,
                    "❌ الاستخدام:\n" +
                    "!مزاج تغيير <احمر|ازرق|اصفر|بنفسجي>"
                );

                return;
            }

            string userId =
                message.UserId;

            string nickname =
                await GetNickname(
                    client,
                    userId
                );

            string newTeamName =
                NormalizeTeam(parts[1]);

            Team? newTeam =
                _game.Teams.FirstOrDefault(
                    x => x.Name == newTeamName
                );

            if (newTeam == null)
            {
                await client.Reply(
                    message,
                    "❌ الفريق غير موجود."
                );

                return;
            }

            foreach (Team team in _game.Teams)
            {
                if (team.Players.Remove(userId))
                {
                    newTeam.Players[userId] =
                        nickname;

                    await client.Reply(
                        message,
                        $"🔄 تم تغيير فريقك إلى " +
                        $"{newTeam.Emoji} " +
                        $"{newTeam.Name}"
                    );

                    return;
                }
            }

            newTeam.Players[userId] =
                nickname;

            await client.Reply(
                message,
                $"✅ تم تسجيلك في " +
                $"{newTeam.Emoji} " +
                $"{newTeam.Name}"
            );
        }

        // =========================================================
        // عرض اللاعبين
        // =========================================================

        private static async Task ShowPlayers(
            IWolfClient client,
            Message message)
        {
            if (_game == null)
            {
                await client.Reply(
                    message,
                    "❌ لا توجد لعبة."
                );

                return;
            }

            string result =
                "👥 لاعبو لعبة مزاج\n\n";

            foreach (Team team in _game.Teams)
            {
                result +=
                    $"{team.Emoji} {team.Name}\n";

                if (team.Players.Count == 0)
                {
                    result +=
                        "   لا يوجد لاعبين\n";
                }
                else
                {
                    int index = 1;

                    foreach (
                        string name
                        in team.Players.Values)
                    {
                        result +=
                            $"   {index}. {name}\n";

                        index++;
                    }
                }

                result += "\n";
            }

            await client.Reply(
                message,
                result.TrimEnd()
            );
        }

        // =========================================================
        // بدء اللعبة
        // =========================================================

        private static async Task StartGame(
            IWolfClient client,
            Message message)
        {
            if (_game == null)
            {
                await client.Reply(
                    message,
                    "❌ لا توجد لعبة."
                );

                return;
            }

            if (_game.Started)
            {
                await client.Reply(
                    message,
                    "⚠️ اللعبة بدأت بالفعل."
                );

                return;
            }

            // =====================================================
            // بناء ترتيب اللاعبين أولاً
            // =====================================================

            _game.TurnOrder.Clear();

            foreach (Team team in _game.Teams)
            {
                foreach (
                    string userId
                    in team.Players.Keys)
                {
                    _game.TurnOrder.Add(
                        userId
                    );
                }
            }

            if (_game.TurnOrder.Count == 0)
            {
                await client.Reply(
                    message,
                    "❌ يجب أن ينضم لاعب واحد على الأقل."
                );

                return;
            }

            _game.Started = true;

            _game.CurrentPlayerIndex = 0;

            _game.TurnVersion++;

            string firstPlayer =
                _game.CurrentPlayerName;

            string result =
                "🎭🔥 لعبة مزاج بدأت ⚡\n\n" +
                $"👥 مجموع اللاعبين: " +
                $"{_game.TurnOrder.Count}\n\n" +
                BuildScoreBoard(_game) +
                "\n\n" +
                "🎴 لوحة الأرقام\n" +
                BuildCardBoard(_game) +
                "\n\n" +
                $"👤 اللاعب التالي: {firstPlayer}\n" +
                "⏱️ عندك 25 ثانية تختار واحد من الأرقام";

            await client.Reply(
                message,
                result
            );

            _ = StartTurnTimer(
                client,
                _game
            );
        }

        // =========================================================
        // اختيار البطاقة
        // =========================================================

        private static async Task ChooseCard(
            IWolfClient client,
            Message message,
            int number,
            bool directNumber)
        {
            if (_game == null ||
                !_game.Started)
            {
                return;
            }

            MazajGame game =
                _game;

            if (number < 1 ||
                number > 65)
            {
