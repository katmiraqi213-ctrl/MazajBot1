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

                    // =================================================
                    // الأرقام المباشرة
                    // =================================================

                    if (TryParseNumber(text, out int directNumber))
                    {
                        if (_game != null && _game.Started)
                        {
                            await ChooseCard(
                                client,
                                message,
                                directNumber,
                                true
                            );
                        }

                        // خارج اللعبة أو اللاعب ليس صاحب الدور:
                        // تجاهل بصمت
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
                        "❌ COMMAND ERROR: " + ex.Message
                    );
                }
            };

            // =====================================================
            // تسجيل الدخول
            // =====================================================

            Console.WriteLine("🔐 تسجيل الدخول...");

            bool loginResult =
                await _client.Login(
                    email,
                    password
                );

            if (!loginResult)
            {
                Console.WriteLine(
                    "❌ فشل تسجيل الدخول إلى Wolf."
                );

                return;
            }

            Console.WriteLine(
                "✅ تم تسجيل الدخول بنجاح."
            );

            // =====================================================
            // الاتصال
            // =====================================================

            await _client.Connect();

            Console.WriteLine(
                "✅ Mazaj Bot يعمل الآن."
            );

            // إبقاء البرنامج يعمل
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
                await SendHelp(client, message);
                return;
            }

            string action =
                parts[0].ToLowerInvariant();

            switch (action)
            {
                case "جديد":
                    await NewGame(
                        client,
                        message
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
        // إنشاء اللعبة
        // =========================================================

        private static async Task NewGame(
            IWolfClient client,
            Message message)
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

            _game = new MazajGame();

            // حفظ الروم الذي أنشئت منه اللعبة
            _game.GroupId =
                message.GroupId ?? "";

            await client.Reply(
                message,
                "🎭🔥 تم إنشاء لعبة مزاج!\n\n" +
                "🟥 400 نقطة\n" +
                "🟦 400 نقطة"
            );
        }

        // =========================================================
        // الانضمام إلى الفريق
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
                    "!مزاج انضم احمر\n" +
                    "!مزاج انضم ازرق"
                );

                return;
            }

            string userId =
                message.UserId;

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
                    "❌ الفريق غير موجود.\n\n" +
                    "المتاح فقط:\n" +
                    "🟥 احمر\n" +
                    "🟦 ازرق"
                );

                return;
            }

            string nickname =
                await GetNickname(
                    client,
                    userId
                );

            // التأكد أن اللاعب غير موجود مسبقاً
            foreach (Team existingTeam in _game.Teams)
            {
                if (existingTeam.Players.ContainsKey(userId))
                {
                    await client.Reply(
                        message,
                        $"⚠️ أنت منضم مسبقاً إلى " +
                        $"{existingTeam.Emoji} " +
                        $"{existingTeam.DisplayName}"
                    );

                    return;
                }
            }

            team.Players[userId] =
                nickname;

            await client.Reply(
                message,
                $"✅ تم انضمامك إلى " +
                $"{team.Emoji} " +
                $"{team.DisplayName}\n" +
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
                    "❌ لا توجد لعبة."
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
                    "!مزاج تغيير احمر\n" +
                    "!مزاج تغيير ازرق"
                );

                return;
            }

            string userId =
                message.UserId;

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

            string nickname =
                await GetNickname(
                    client,
                    userId
                );

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
                        $"{newTeam.DisplayName}"
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
                $"{newTeam.DisplayName}"
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
                    $"{team.Emoji} {team.DisplayName}\n";

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

            // يجب وجود لاعب في الفريقين
            if (_game.Teams.Any(
                    x => x.Players.Count == 0))
            {
                await client.Reply(
                    message,
                    "❌ يجب أن يكون هناك لاعب " +
                    "واحد على الأقل في كل فريق.\n\n" +
                    "🟥 الأمراء\n" +
                    "🟦 النجوم"
                );

                return;
            }

            // إنشاء ترتيب الأدوار
            _game.TurnOrder.Clear();

            foreach (Team team in _game.Teams)
            {
                foreach (string userId
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
                    "❌ لا يوجد لاعبون."
                );

                return;
            }

            _game.Started = true;

            _game.CurrentPlayerIndex = 0;

            _game.TurnVersion++;

            string firstPlayer =
                _game.CurrentPlayerName;

            string result =
                "🎭🔥 لعبة مزاج بدأت!\n\n" +
                "🎴 لوحة الأرقام\n" +
                BuildCardBoard(_game) +
                "\n\n" +
                $"👤 اللاعب التالي: {firstPlayer}\n" +
                "⏱️ عندك 25 ثانية تختار واحد من الأرقام";

            await client.Reply(
                message,
                result
            );

            // تشغيل مؤقت أول لاعب
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

            // الرقم خارج المدى
            if (number < 1 ||
                number > 65)
            {
                if (!directNumber)
                {
                    await client.Reply(
                        message,
                        "❌ رقم البطاقة يجب أن يكون " +
                        "من 1 إلى 65."
                    );
                }

                return;
            }

            string userId =
                message.UserId;

            // يجب أن يكون مشاركاً
            if (!game.TurnOrder.Contains(
                    userId))
            {
                // الرقم المباشر يتجاهل بصمت
                if (!directNumber)
                {
                    await client.Reply(
                        message,
                        "❌ أنت لست مشاركاً في اللعبة."
                    );
                }

                return;
            }

            // يجب أن يكون دوره
            if (game.CurrentPlayerId != userId)
            {
                // الأرقام المباشرة من الآخرين
                // يتم تجاهلها بدون رد
                if (!directNumber)
                {
                    await client.Reply(
                        message,
                        $"⏳ ليس دورك.\n" +
                        $"👤 الدور حالياً: " +
                        $"{game.CurrentPlayerName}"
                    );
                }

                return;
            }

            Card? card =
                game.Cards.FirstOrDefault(
                    x => x.Number == number
                );

            if (card == null)
            {
                return;
            }

            // البطاقة مستخدمة
            if (card.Used)
            {
                if (!directNumber)
                {
                    await client.Reply(
                        message,
                        "❌ هذه البطاقة تم اختيارها مسبقاً."
                    );
                }

                return;
            }

            Team? playerTeam =
                game.GetTeamByPlayer(
                    userId
                );

            if (playerTeam == null)
            {
                return;
            }

            // تسجيل البطاقة
            card.Used = true;

            // =====================================================
            // طريقة احتساب النقاط
            //
            // البطاقة الموجبة:
            // الخصم يخسر النقاط
            // فريق اللاعب لا تزيد نقاطه
            //
            // البطاقة السالبة:
            // فريق اللاعب يخسر النقاط
            // =====================================================

            Team? opponentTeam =
                game.GetOpponentTeam(
                    playerTeam
                );

            string scoreMessage;

            if (card.Value > 0)
            {
                if (opponentTeam != null)
                {
                    opponentTeam.Score -=
                        card.Value;

                    scoreMessage =
                        $"{playerTeam.Emoji} " +
                        $"{playerTeam.DisplayName} " +
                        $"ضربوا الخصم بـ {card.Value}\n" +
                        $"{opponentTeam.Emoji} " +
                        $"{opponentTeam.DisplayName} " +
                        $"خسروا {card.Value} نقطة";
                }
                else
                {
                    scoreMessage =
                        $"💥 ضربة بـ {card.Value} نقطة";
                }
            }
            else
            {
                int loss =
                    Math.Abs(card.Value);

                playerTeam.Score -=
                    loss;

                scoreMessage =
                    $"{playerTeam.Emoji} " +
                    $"{playerTeam.DisplayName} " +
                    $"خسروا {loss} نقطة";
            }

            // إلغاء المؤقت القديم
            game.TurnVersion++;

            string result =
                "/me\n\n" +
                $"🎴 {card.Number}\n" +
                $"🃏 {card.Name}\n" +
                $"💰 القيمة: {FormatValue(card.Value)}\n\n" +
                $"{scoreMessage}";

            // =====================================================
            // هل انتهت كل البطاقات؟
            // =====================================================

            if (game.AllCardsUsed)
            {
                game.Started = false;

                game.TurnVersion++;

                result +=
                    "\n\n🏁 انتهت جميع البطاقات!\n\n" +
                    BuildFinalResults(game);

                _game = null;

                await client.Reply(
                    message,
                    result
                );

                return;
            }

            // =====================================================
            // الانتقال للاعب التالي
            // =====================================================

            game.CurrentPlayerIndex++;

            if (game.CurrentPlayerIndex >=
                game.TurnOrder.Count)
            {
                game.CurrentPlayerIndex = 0;
            }

            string nextPlayer =
                game.CurrentPlayerName;

            // إرسال نتيجة البطاقة أولاً
            await client.Reply(
                message,
                result
            );

            // انتظار ثانيتين
            await Task.Delay(
                TimeSpan.FromSeconds(2)
            );

            // التأكد أن اللعبة لم تتغير
            if (_game != game ||
                !game.Started)
            {
                return;
            }

            // إرسال اللوحة الجديدة
            await SendBoards(
                client,
                game
            );

            // تشغيل مؤقت اللاعب الجديد
            _ = StartTurnTimer(
                client,
                game
            );
        }

        // =========================================================
        // مؤقت 25 ثانية + طرد اللاعب
        // =========================================================

        private static async Task StartTurnTimer(
            IWolfClient client,
            MazajGame game)
        {
            int version =
                game.TurnVersion;

            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(25)
                );

                // اللعبة تغيرت أو اللاعب اختار
                if (_game != game ||
                    !game.Started ||
                    game.TurnVersion != version)
                {
                    return;
                }

                if (game.TurnOrder.Count == 0)
                {
                    return;
                }

                // اللاعب الحالي
                string oldPlayerId =
                    game.CurrentPlayerId;

                string oldPlayer =
                    game.CurrentPlayerName;

                if (string.IsNullOrWhiteSpace(
                        oldPlayerId))
                {
                    return;
                }

                // =================================================
                // طرد اللاعب من اللعبة
                // =================================================

                foreach (Team team in game.Teams)
                {
                    team.Players.Remove(
                        oldPlayerId
                    );
                }

                // حذف اللاعب من ترتيب الأدوار
                game.TurnOrder.Remove(
                    oldPlayerId
                );

                game.TurnVersion++;

                // =================================================
                // إذا صار فريق بدون لاعبين
                // =================================================

                Team? emptyTeam =
                    game.Teams.FirstOrDefault(
                        x => x.Players.Count == 0
                    );

                if (emptyTeam != null)
                {
                    game.Started = false;

                    Team? winner =
                        game.Teams.FirstOrDefault(
                            x => x != emptyTeam
                        );

                    string result;

                    if (winner != null)
                    {
                        result =
                            "🏁 انتهت لعبة مزاج!\n\n" +
                            $"⏰ انتهى وقت اللاعب: {oldPlayer}\n" +
                            $"🚫 تم طرد {oldPlayer} " +
                            "من اللعبة بسبب عدم اختيار بطاقة خلال 25 ثانية.\n\n" +
                            $"❌ {emptyTeam.Emoji} " +
                            $"{emptyTeam.DisplayName} " +
                            "لم يعد لديهم لاعبين.\n\n" +
                            $"🏆 الفائز: {winner.Emoji} " +
                            $"{winner.DisplayName}\n\n" +
                            $"🟥 الأمراء: " +
                            $"{game.Teams[0].Score} نقطة\n" +
                            $"🟦 النجوم: " +
                            $"{game.Teams[1].Score} نقطة";
                    }
                    else
                    {
                        result =
                            "🏁 انتهت لعبة مزاج!\n\n" +
                            $"🚫 تم طرد {oldPlayer}.\n\n" +
                            "🤝 لا يوجد فائز.";
                    }

                    _game = null;

                    if (!string.IsNullOrWhiteSpace(
                            game.GroupId))
                    {
                        await client.GroupMessage(
                            game.GroupId,
                            result
                        );
                    }

                    return;
                }

                // =================================================
                // إذا بقي لاعبون
                // =================================================

                if (game.TurnOrder.Count == 0)
                {
                    game.Started = false;

                    _game = null;

                    return;
                }

                // بعد حذف اللاعب الحالي
                // لا نزيد CurrentPlayerIndex
                // لأن اللاعب الذي حل مكانه أخذ نفس
                // رقم الدور

                if (game.CurrentPlayerIndex >=
                    game.TurnOrder.Count)
                {
                    game.CurrentPlayerIndex = 0;
                }

                string nextPlayer =
                    game.CurrentPlayerName;

                string resultMessage =
                    $"⏰ انتهى وقت اللاعب: {oldPlayer}\n\n" +
                    $"🚫 تم طرد {oldPlayer} " +
                    "من اللعبة بسبب عدم اختيار بطاقة خلال 25 ثانية.\n\n" +
                    "🎴 لوحة الأرقام\n" +
                    BuildCardBoard(game) +
                    "\n\n" +
                    $"👤 اللاعب التالي: {nextPlayer}\n" +
                    "⏱️ عندك 25 ثانية تختار واحد من الأرقام";

                if (!string.IsNullOrWhiteSpace(
                        game.GroupId))
                {
                    await client.GroupMessage(
                        game.GroupId,
                        resultMessage
                    );
                }

                // مؤقت اللاعب الجديد
                _ = StartTurnTimer(
                    client,
                    game
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    "❌ TURN TIMER ERROR: " +
                    ex.Message
                );
            }
        }

        // =========================================================
        // إرسال لوحة الأرقام واللاعب التالي
        // =========================================================

        private static async Task SendBoards(
            IWolfClient client,
            MazajGame game)
        {
            if (string.IsNullOrWhiteSpace(
                    game.GroupId))
            {
                return;
            }

            string nextPlayer =
                game.CurrentPlayerName;

            string result =
                "🎴 لوحة الأرقام\n" +
                BuildCardBoard(game) +
                "\n\n" +
                $"👤 اللاعب التالي: {nextPlayer}\n" +
                "⏱️ عندك 25 ثانية تختار واحد من الأرقام";

            await client.GroupMessage(
                game.GroupId,
                result
            );
        }

        // =========================================================
        // إنهاء اللعبة يدوياً
        // =========================================================

        private static async Task EndGame(
            IWolfClient client,
            Message message)
        {
            if (_game == null)
            {
                await client.Reply(
                    message,
                    "❌ لا توجد لعبة حالياً."
                );

                return;
            }

            MazajGame game =
                _game;

            game.Started = false;

            // إلغاء المؤقت
            game.TurnVersion++;

            string result =
                "🛑 تم إنهاء لعبة مزاج.\n\n" +
                BuildFinalResults(game);

            _game = null;

            await client.Reply(
                message,
                result
            );
        }

        // =========================================================
        // لوحة الأرقام
        // =========================================================

        private static string BuildCardBoard(
            MazajGame game)
        {
            string result = "";

            string[] colors =
            {
                "🟥",
                "🟦",
                "🟨",
                "🟪"
            };

            for (int i = 1; i <= 65; i++)
            {
                Card card =
                    game.Cards[i - 1];

                string display;

                if (card.Used)
                {
                    display = "❌";
                }
                else
                {
                    string color =
                        colors[(i - 1) % 4];

                    display =
                        color + i;
                }

                result += display;

                if (i < 65)
                {
                    if (i % 7 == 0)
                    {
                        result += "\n";
                    }
                    else
                    {
                        result += " ";
                    }
                }
            }

            return result;
        }

        // =========================================================
        // عرض البطاقات
        // =========================================================

        private static async Task ShowCards(
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
                "🎴 بطاقات لعبة مزاج\n\n";

            foreach (Card card
                in _game.Cards)
            {
                result +=
                    $"{card.Number}. " +
                    $"{card.Name} " +
                    $"({FormatValue(card.Value)})\n";
            }

            await client.Reply(
                message,
                result
            );
        }

        // =========================================================
        // المساعدة
        // =========================================================

        private static async Task SendHelp(
            IWolfClient client,
            Message message)
        {
            string help =
                "🎭🔥 أوامر لعبة مزاج\n\n" +

                "🎮 إنشاء اللعبة:\n" +
                "!مزاج جديد\n\n" +

                "👥 الانضمام:\n" +
                "!مزاج انضم احمر\n" +
                "!مزاج انضم ازرق\n\n" +

                "🔄 تغيير الفريق:\n" +
                "!مزاج تغيير احمر\n" +
                "!مزاج تغيير ازرق\n\n" +

                "👥 عرض اللاعبين:\n" +
                "!مزاج لاعبين\n\n" +

                "▶️ بدء اللعبة:\n" +
                "!مزاج بدء\n\n" +

                "🎴 اختيار بطاقة:\n" +
                "اكتب الرقم مباشرة مثل:\n" +
                "13\n\n" +

                "أو:\n" +
                "!مزاج اختار 13\n\n" +

                "🃏 عرض البطاقات:\n" +
                "!مزاج بطاقات\n\n" +

                "🛑 إنهاء اللعبة:\n" +
                "!مزاج انهاء\n\n" +

                "⏱️ مدة الدور: 25 ثانية\n" +
                "🚫 إذا لم تختار خلال 25 ثانية يتم طردك من اللعبة.";

            await client.Reply(
                message,
                help
            );
        }

        // =========================================================
        // النتائج النهائية
        // =========================================================

        private static string BuildFinalResults(
            MazajGame game)
        {
            Team red =
                game.Teams[0];

            Team blue =
                game.Teams[1];

            string result =
                "🏆 النتائج النهائية\n\n" +
                $"🟥 {red.DisplayName}: " +
                $"{red.Score} نقطة\n" +
                $"🟦 {blue.DisplayName}: " +
                $"{blue.Score} نقطة\n\n";

            if (red.Score > blue.Score)
            {
                result +=
                    $"👑 الفائز: 🟥 " +
                    $"{red.DisplayName}";
            }
            else if (blue.Score > red.Score)
            {
                result +=
                    $"👑 الفائز: 🟦 " +
                    $"{blue.DisplayName}";
            }
            else
            {
                result +=
                    "🤝 النتيجة: تعادل";
            }

            return result;
        }

        // =========================================================
        // أسماء الفرق
        // =========================================================

        private static string NormalizeTeam(
            string value)
        {
            return value
                .Trim()
                .ToLowerInvariant()
                .Replace("أ", "ا")
                .Replace("إ", "ا")
                .Replace("آ", "ا")
                switch
            {
                "احمر" => "احمر",
                "الاحمر" => "احمر",

                "ازرق" => "ازرق",
                "الازرق" => "ازرق",

                _ => value.Trim()
            };
        }

        // =========================================================
        // قراءة الرقم
        // =========================================================

        private static bool TryParseNumber(
            string text,
            out int number)
        {
            number = 0;

            if (string.IsNullOrWhiteSpace(
                    text))
            {
                return false;
            }

            return int.TryParse(
                text.Trim(),
                out number
            );
        }

        // =========================================================
        // تنسيق النقاط
        // =========================================================

        private static string FormatValue(
            int value)
        {
            return value >= 0
                ? $"+{value}"
                : value.ToString();
        }

        // =========================================================
        // اسم اللاعب
        // =========================================================

        private static async Task<string> GetNickname(
            IWolfClient client,
            string userId)
        {
            try
            {
                var user =
                    await client.GetUser(userId);

                if (user != null &&
                    !string.IsNullOrWhiteSpace(
                        user.Nickname))
                {
                    return user.Nickname;
                }
            }
            catch
            {
                // تجاهل الخطأ
            }

            return userId;
        }
    }

    // =================================================================
    // MazajGame
    // =================================================================

    public class MazajGame
    {
        public int PointsPerCard
        {
            get;
        } = 0;

        public int TeamCount
        {
            get;
        } = 2;

        public List<Team> Teams
        {
            get;
        }

        public List<Card> Cards
        {
            get;
        }

        public List<string> TurnOrder
        {
            get;
        }

        public bool Started
        {
            get;
            set;
        }

        public int CurrentPlayerIndex
        {
            get;
            set;
        }

        public int TurnVersion
        {
            get;
            set;
        }

        public string GroupId
        {
            get;
            set;
        } = "";

        public MazajGame()
        {
            Teams =
                new List<Team>
                {
                    new Team(
                        "احمر",
                        "🟥",
                        "الأمراء"
                    ),

                    new Team(
                        "ازرق",
                        "🟦",
                        "النجوم"
                    )
                };

            TurnOrder =
                new List<string>();

            Cards =
                CreateCards();

            // البداية 400 لكل فريق
            foreach (Team team in Teams)
            {
                team.Score = 400;
            }
        }

        // -------------------------------------------------------------
        // اللاعب الحالي
        // -------------------------------------------------------------

        public string CurrentPlayerId
        {
            get
            {
                if (TurnOrder.Count == 0)
                {
                    return "";
                }

                if (CurrentPlayerIndex < 0 ||
                    CurrentPlayerIndex >=
                    TurnOrder.Count)
                {
                    return "";
                }

                return TurnOrder[
                    CurrentPlayerIndex
                ];
            }
        }

        public string CurrentPlayerName
        {
            get
            {
                string userId =
                    CurrentPlayerId;

                if (string.IsNullOrWhiteSpace(
                        userId))
                {
                    return "";
                }

                foreach (Team team in Teams)
                {
                    if (team.Players.TryGetValue(
                            userId,
                            out string? name))
                    {
                        return name;
                    }
                }

                return userId;
            }
        }

        // -------------------------------------------------------------
        // فريق اللاعب
        // -------------------------------------------------------------

        public Team? GetTeamByPlayer(
            string userId)
        {
            return Teams.FirstOrDefault(
                x => x.Players.ContainsKey(
                    userId
                )
            );
        }

        // -------------------------------------------------------------
        // الفريق الخصم
        // -------------------------------------------------------------

        public Team? GetOpponentTeam(
            Team playerTeam)
        {
            return Teams.FirstOrDefault(
                x => x != playerTeam
            );
        }

        // -------------------------------------------------------------
        // هل انتهت البطاقات؟
        // -------------------------------------------------------------

        public bool AllCardsUsed
        {
            get
            {
                return Cards.All(
                    x => x.Used
                );
            }
        }

        // -------------------------------------------------------------
        // إنشاء 65 بطاقة
        // -------------------------------------------------------------

        private static List<Card> CreateCards()
        {
            List<Card> cards =
                new List<Card>();

            // =========================================================
            // 57 بطاقة موجبة
            // =========================================================

            int[] positiveValues =
            {
                100,
                90,
                85,

                80, 80, 80,

                75, 75, 75,

                70, 70, 70, 70,

                65, 65, 65, 65,

                60, 60, 60, 60,

                55, 55, 55, 55,

                50, 50, 50, 50, 50,

                45, 45, 45, 45,

                40, 40, 40, 40,

                35, 35, 35, 35,

                30, 30, 30, 30,

                25, 25, 25, 25,

                20, 20, 20, 20,

                15, 15, 15,

                10, 10, 10
            };

            // =========================================================
            // 8 بطاقات سالبة
            // =========================================================

            int[] negativeValues =
            {
                -10,
                -15,
                -20,
                -25,
                -30,
                -35,
                -40,
                -50
            };

            // =========================================================
            // البطاقات الموجبة
            // =========================================================

            for (int i = 0;
                 i < positiveValues.Length;
                 i++)
            {
                string name;

                if (i == 0)
                {
                    name =
                        "ضربة الوحش محمد 🇮🇶❤️";
                }
                else if (i == 1)
                {
                    name =
                        "ضربة يوسف المهندس";
                }
                else if (i == 2)
                {
                    name =
                        "ضربة سرمد الوحش 🔥";
                }
                else
                {
                    name =
                        $"ضربة مزاج {i + 1}";
                }

                cards.Add(
                    new Card(
                        cards.Count + 1,
                        name,
                        positiveValues[i]
                    )
                );
            }

            // =========================================================
            // البطاقات السالبة
            // =========================================================

            for (int i = 0;
                 i < negativeValues.Length;
                 i++)
            {
                cards.Add(
                    new Card(
                        cards.Count + 1,
                        $"بطاقة نحس {cards.Count + 1}",
                        negativeValues[i]
                    )
                );
            }

            return cards;
        }
    }

    // =================================================================
    // Team
    // =================================================================

    public class Team
    {
        public string Name
        {
            get;
        }

        public string Emoji
        {
            get;
        }

        public string DisplayName
        {
            get;
        }

        public int Score
        {
            get;
            set;
        }

        public Dictionary<string, string> Players
        {
            get;
        }

        public Team(
            string name,
            string emoji,
            string displayName)
        {
            Name = name;

            Emoji = emoji;

            DisplayName =
                displayName;

            Score = 400;

            Players =
                new Dictionary<
                    string,
                    string>();
        }
    }

    // =================================================================
    // Card
    // =================================================================

    public class Card
    {
        public int Number
        {
            get;
        }

        public string Name
        {
            get;
        }

        public int Value
        {
            get;
        }

        public bool Used
        {
            get;
            set;
        }

        public Card(
            int number,
            string name,
            int value)
        {
            Number = number;

            Name = name;

            Value = value;

            Used = false;
        }
    }
}
