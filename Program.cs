using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WolfLive.Api;
using WolfLive.Api.Models;

namespace MazajBot
{
    public class Program
    {
        private static IWolfClient? _client;
        private static MazajGame? _game;

        private static readonly HashSet<string> _processedMessages = new();
        private static readonly object _messageLock = new();

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

            Console.WriteLine("🔐 جاري تسجيل الدخول إلى Wolf...");

            bool loginResult =
                await _client.Login(email, password);

            if (!loginResult)
            {
                Console.WriteLine(
                    "❌ فشل تسجيل الدخول إلى Wolf. تأكد من WOLF_EMAIL و WOLF_PASSWORD."
                );

                return;
            }

            Console.WriteLine(
                "✅ تم تسجيل الدخول بنجاح."
            );

            _client.Messaging.OnMessage += async (client, message) =>
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(message.MessageId))
                    {
                        lock (_messageLock)
                        {
                            if (!_processedMessages.Add(
                                message.MessageId))
                            {
                                return;
                            }

                            if (_processedMessages.Count > 5000)
                            {
                                _processedMessages.Clear();
                            }
                        }
                    }

                    string text =
                        message.Content?.Trim() ?? "";

                    // =========================================
                    // الأرقام المباشرة
                    // =========================================

                    if (TryParseNumber(
                        text,
                        out int directNumber))
                    {
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

                    // =========================================
                    // أوامر مزاج
                    // =========================================

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
                        "COMMAND ERROR: " +
                        ex.Message
                    );
                }
            };

            await _client.Connect();

            Console.WriteLine(
                "🟢 Mazaj Bot يعمل الآن."
            );

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
                        "❌ أمر غير معروف.\nاكتب !مزاج مساعدة"
                    );

                    break;
            }
        }

        // =========================================================
        // المساعدة
        // =========================================================

        private static async Task SendHelp(
            IWolfClient client,
            Message message)
        {
            string help =
                "🎭 أوامر لعبة مزاج\n\n" +

                "!مزاج جديد — إنشاء لعبة جديدة\n" +

                "!مزاج انضم احمر — الانضمام إلى الأمراء 🟥\n" +

                "!مزاج انضم ازرق — الانضمام إلى النجوم 🟦\n" +

                "!مزاج تغيير احمر/ازرق — تغيير الفريق\n" +

                "!مزاج لاعبين — عرض اللاعبين\n" +

                "!مزاج بدء — بدء اللعبة\n" +

                "!مزاج اختار <رقم> — اختيار بطاقة\n" +

                "!مزاج بطاقات — عرض البطاقات المتبقية\n" +

                "!مزاج انهاء — إنهاء اللعبة\n\n" +

                "🎴 أثناء الدور يمكنك إرسال رقم البطاقة مباشرة من 1 إلى 65.";

            await client.Reply(
                message,
                help
            );
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
                    "⚠️ توجد لعبة حالياً.\nاستخدم !مزاج انهاء أولاً."
                );

                return;
            }

            _game = new MazajGame();

            _game.GroupId =
                message.GroupId ?? "";

            await client.Reply(
                message,

                "🎭🔥 تم إنشاء لعبة مزاج!\n\n" +

                "🟥 400 نقطة\n" +
                "🟦 400 نقطة\n\n" +

                "📌 الانضمام:\n" +

                "!مزاج انضم احمر\n" +
                "!مزاج انضم ازرق\n\n" +

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
                    "❌ الاستخدام:\n!مزاج انضم <احمر|ازرق>"
                );

                return;
            }

            string teamCode =
                NormalizeTeam(parts[1]);

            Team? team =
                _game.Teams.FirstOrDefault(
                    t => t.Code == teamCode
                );

            if (team == null)
            {
                await client.Reply(
                    message,
                    "❌ الفريق غير موجود. اختر احمر أو ازرق."
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

            foreach (Team existing in _game.Teams)
            {
                if (existing.Players.ContainsKey(
                    userId))
                {
                    await client.Reply(
                        message,

                        $"⚠️ أنت منضم مسبقاً إلى " +
                        $"{existing.Emoji} " +
                        $"{existing.Name}."
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
                $"{team.Name}\n" +

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
                    "❌ الاستخدام:\n!مزاج تغيير <احمر|ازرق>"
                );

                return;
            }

            string newCode =
                NormalizeTeam(parts[1]);

            Team? newTeam =
                _game.Teams.FirstOrDefault(
                    t => t.Code == newCode
                );

            if (newTeam == null)
            {
                await client.Reply(
                    message,
                    "❌ الفريق غير موجود. اختر احمر أو ازرق."
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

            foreach (Team team in _game.Teams)
            {
                if (team.Players.Remove(
                    userId))
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
                    $"{team.Emoji} " +
                    $"{team.Name} " +
                    $"({team.Players.Count} لاعبين)\n";

                if (team.Players.Count == 0)
                {
                    result +=
                        "لا يوجد لاعبين\n\n";

                    continue;
                }

                int index = 1;

                foreach (string name in team.Players.Values)
                {
                    result +=
                        $"{index}. {name}\n";

                    index++;
                }

                result += "\n";
            }

            await client.Reply(
                message,
                result
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

            if (_game.Teams[0].Players.Count == 0 ||
                _game.Teams[1].Players.Count == 0)
            {
                await client.Reply(
                    message,

                    "❌ لازم يكون أكو لاعب واحد على الأقل بكل فريق.\n\n" +

                    "🟥 الأمراء\n" +
                    "🟦 النجوم"
                );

                return;
            }

            _game.TurnOrder.Clear();

            // ترتيب اللاعبين حسب الفرق.
            foreach (Team team in _game.Teams)
            {
                foreach (string userId in team.Players.Keys)
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
                    "❌ لا يوجد لاعبين."
                );

                return;
            }

            _game.Started = true;
            _game.CurrentPlayerIndex = 0;
            _game.TurnVersion++;

            string board =
                BuildCardBoard(_game);

            string current =
                _game.CurrentPlayerName;

            string result =
                "🎭🔥 بدأت لعبة مزاج!\n\n" +

                "🟥 الأمراء: 400 نقطة\n" +
                "🟦 النجوم: 400 نقطة\n\n" +

                "🎴 لوحة الأرقام\n" +

                board +

                "\n\n" +

                $"🎯 الدور: {current}\n" +

                "⏱️ عندك 25 ثانية تختار رقم";

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
                if (!directNumber)
                {
                    await client.Reply(
                        message,
                        "❌ لا توجد لعبة قيد التشغيل."
                    );
                }

                return;
            }

            MazajGame game =
                _game;

            if (number < 1 ||
                number > 65)
            {
                if (!directNumber)
                {
                    await client.Reply(
                        message,
                        "❌ اختر رقم من 1 إلى 65."
                    );
                }

                return;
            }

            string userId =
                message.UserId;

            if (game.CurrentPlayerId != userId)
            {
                if (!directNumber)
                {
                    await client.Reply(
                        message,
                        "⏳ مو دورك حالياً."
                    );
                }

                return;
            }

            Card? card =
                game.Cards.FirstOrDefault(
                    c => c.Number == number
                );

            if (card == null)
                return;

            if (card.Used)
            {
                if (!directNumber)
                {
                    await client.Reply(
                        message,
                        "❌ هذا الرقم مستخدم، اختار رقم ثاني."
                    );
                }

                return;
            }

            // إيقاف مؤقت الدور القديم.
            game.TurnVersion++;

            card.Used = true;

            Team? currentTeam =
                game.GetTeamByPlayer(
                    userId
                );

            if (currentTeam == null)
                return;

            Team opponent =
                game.Teams.First(
                    t => t != currentTeam
                );

            string playerName =
                game.CurrentPlayerName;

            string result;

            // =====================================================
            // بطاقة موجبة
            // الخصم يخسر النقاط
            // =====================================================

            if (card.Value > 0)
            {
                opponent.Score -=
                    card.Value;

                if (opponent.Score < 0)
                    opponent.Score = 0;

                result =
                    "🎴🔥 تم اختيار البطاقة!\n\n" +

                    $"👤 اللاعب: {playerName}\n" +

                    $"🔢 الرقم: {card.Number}\n" +

                    $"💥 {card.Name}\n\n" +

                    $"➕ قيمة البطاقة: +{card.Value}\n\n" +

                    $"🟥 {game.Teams[0].Score} نقطة\n" +
                    $"🟦 {game.Teams[1].Score} نقطة";
            }
            else
            {
                // =================================================
                // بطاقة سالبة
                // فريق اللاعب يخسر النقاط
                // =================================================

                int loss =
                    Math.Abs(card.Value);

                currentTeam.Score -= loss;

                if (currentTeam.Score < 0)
                    currentTeam.Score = 0;

                result =
                    "🎴💀 بطاقة سالبة!\n\n" +

                    $"👤 اللاعب: {playerName}\n" +

                    $"🔢 الرقم: {card.Number}\n" +

                    $"💥 {card.Name}\n\n" +

                    $"➖ خسارة: {loss} نقطة\n\n" +

                    $"🟥 {game.Teams[0].Score} نقطة\n" +
                    $"🟦 {game.Teams[1].Score} نقطة";
            }

            await client.Reply(
                message,
                result
            );

            // =====================================================
            // فوز بالنقاط
            // =====================================================

            if (game.Teams.Any(
                t => t.Score <= 0))
            {
                game.Started = false;
                game.TurnVersion++;

                string winnerText =
                    GetWinnerText(game);

                await client.Reply(
                    message,
                    winnerText
                );

                _game = null;

                return;
            }

            // =====================================================
            // انتهاء كل البطاقات
            // =====================================================

            if (game.AllCardsUsed)
            {
                game.Started = false;
                game.TurnVersion++;

                string winnerText =
                    GetWinnerText(game);

                await client.Reply(
                    message,
                    winnerText
                );

                _game = null;

                return;
            }

            await Task.Delay(
                TimeSpan.FromSeconds(2)
            );

            if (_game != game ||
                !game.Started)
            {
                return;
            }

            // =====================================================
            // عرض اللوحة بعد البطاقة
            // =====================================================

            game.CurrentPlayerIndex++;

            if (game.CurrentPlayerIndex >=
                game.TurnOrder.Count)
            {
                game.CurrentPlayerIndex = 0;
            }

            string nextPlayer =
                game.CurrentPlayerName;

            string nextResult =
                "🎴 لوحة الأرقام\n" +

                BuildCardBoard(game) +

                "\n\n" +

                $"🎯 الدور: {nextPlayer}\n" +

                "⏱️ عندك 25 ثانية تختار واحد من الأرقام";

            await client.Reply(
                message,
                nextResult
            );

            _ = StartTurnTimer(
                client,
                game
            );
        }

        // =========================================================
        // مؤقت 25 ثانية
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

                // اللعبة تغيرت أو الدور تغير.
                if (_game != game ||
                    !game.Started ||
                    game.TurnVersion != version)
                {
                    return;
                }

                if (game.TurnOrder.Count == 0)
                    return;

                string oldPlayer =
                    game.CurrentPlayerName;

                string oldPlayerId =
                    game.CurrentPlayerId;

                game.TurnVersion++;

                // =================================================
                // حذف اللاعب الذي لم يختار
                // =================================================

                RemovePlayer(
                    game,
                    oldPlayerId
                );

                if (game.TurnOrder.Count == 0)
                {
                    game.Started = false;

                    if (!string.IsNullOrWhiteSpace(
                        game.GroupId))
                    {
                        await client.GroupMessage(
                            game.GroupId,
                            "⏰ انتهى الوقت.\n❌ لم يبقَ أي لاعب في اللعبة."
                        );
                    }

                    _game = null;
                    return;
                }

                // إذا صار أحد الفرق فارغاً.
                if (game.Teams.Any(
                    t => t.Players.Count == 0))
                {
                    game.Started = false;

                    string result =
                        "🏁 انتهت اللعبة!\n\n" +

                        $"⏰ اللاعب {oldPlayer} لم يختار خلال 25 ثانية.\n\n" +

                        GetWinnerText(game);

                    if (!string.IsNullOrWhiteSpace(
                        game.GroupId))
                    {
                        await client.GroupMessage(
                            game.GroupId,
                            result
                        );
                    }

                    _game = null;
                    return;
                }

                if (game.CurrentPlayerIndex >=
                    game.TurnOrder.Count)
                {
                    game.CurrentPlayerIndex = 0;
                }

                string nextPlayer =
                    game.CurrentPlayerName;

                string timerResult =
                    $"⏰ انتهى وقت اللاعب: {oldPlayer}\n\n" +

                    "🚫 لم يتم اختيار أي بطاقة.\n\n" +

                    "🎴 لوحة الأرقام\n" +

                    BuildCardBoard(game) +

                    "\n\n" +

                    $"🎯 الدور: {nextPlayer}\n" +

                    "⏱️ عندك 25 ثانية تختار واحد من الأرقام";

                if (!string.IsNullOrWhiteSpace(
                    game.GroupId))
                {
                    await client.GroupMessage(
                        game.GroupId,
                        timerResult
                    );
                }

                _ = StartTurnTimer(
                    client,
                    game
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    "TURN TIMER ERROR: " +
                    ex.Message
                );
            }
        }

        // =========================================================
        // حذف لاعب انتهى وقته
        // =========================================================

        private static void RemovePlayer(
            MazajGame game,
            string userId)
        {
            if (string.IsNullOrWhiteSpace(
                userId))
            {
                return;
            }

            foreach (Team team in game.Teams)
            {
                team.Players.Remove(
                    userId
                );
            }

            int removedIndex =
                game.TurnOrder.IndexOf(
                    userId
                );

            if (removedIndex < 0)
                return;

            game.TurnOrder.RemoveAt(
                removedIndex
            );

            if (game.TurnOrder.Count == 0)
            {
                game.CurrentPlayerIndex = 0;
                return;
            }

            if (removedIndex <
                game.CurrentPlayerIndex)
            {
                game.CurrentPlayerIndex--;
            }

            if (game.CurrentPlayerIndex >=
                game.TurnOrder.Count)
            {
                game.CurrentPlayerIndex = 0;
            }
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
                "🎴 البطاقات المتبقية\n\n" +

                BuildCardBoard(_game);

            await client.Reply(
                message,
                result
            );
        }

        // =========================================================
        // إنهاء اللعبة
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

            // إلغاء أي مؤقت شغال.
            game.TurnVersion++;

            string result =
                "🛑 تم إنهاء لعبة مزاج.\n\n" +

                GetWinnerText(game);

            await client.Reply(
                message,
                result
            );

            _game = null;
        }

        // =========================================================
        // لوحة الأرقام
        // =========================================================

        private static string BuildCardBoard(
            MazajGame game)
        {
            List<string> rows =
                new();

            for (int i = 1;
                 i <= 65;
                 i += 8)
            {
                List<string> row =
                    new();

                for (
                    int n = i;
                    n < i + 8 && n <= 65;
                    n++)
                {
                    Card? card =
                        game.Cards.FirstOrDefault(
                            c => c.Number == n
                        );

                    if (card != null &&
                        card.Used)
                    {
                        row.Add("❌");
                    }
                    else
                    {
                        row.Add(
                            n.ToString()
                        );
                    }
                }

                rows.Add(
                    string.Join(
                        " ",
                        row
                    )
                );
            }

            return string.Join(
                "\n",
                rows
            );
        }

        // =========================================================
        // تحديد الفائز
        // =========================================================

        private static string GetWinnerText(
            MazajGame game)
        {
            Team winner =
                game.Teams
                    .OrderByDescending(
                        t => t.Score
                    )
                    .First();

            Team loser =
                game.Teams.First(
                    t => t != winner
                );

            if (winner.Score ==
                loser.Score)
            {
                return
                    "🏁 انتهت لعبة مزاج!\n\n" +

                    "🤝 تعادل الفريقان!\n\n" +

                    $"🟥 {game.Teams[0].Score} نقطة\n" +

                    $"🟦 {game.Teams[1].Score} نقطة";
            }

            return
                "🏁 انتهت لعبة مزاج!\n\n" +

                $"👑 الفائز: " +
                $"{winner.Emoji} " +
                $"{winner.Name}\n\n" +

                $"🏆 النقاط: " +
                $"{winner.Score}\n\n" +

                $"{game.Teams[0].Emoji} " +
                $"{game.Teams[0].Name}: " +
                $"{game.Teams[0].Score}\n" +

                $"{game.Teams[1].Emoji} " +
                $"{game.Teams[1].Name}: " +
                $"{game.Teams[1].Score}";
        }

        // =========================================================
        // توحيد اسم الفريق
        // =========================================================

        private static string NormalizeTeam(
            string value)
        {
            return value
                .Trim()
                .ToLowerInvariant()
                switch
                {
                    "احمر" => "احمر",
                    "الأحمر" => "احمر",
                    "الاحمر" => "احمر",

                    "ازرق" => "ازرق",
                    "الأزرق" => "ازرق",
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

            return
                !string.IsNullOrWhiteSpace(text) &&
                int.TryParse(
                    text.Trim(),
                    out number
                );
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
                    await client.GetUser(
                        userId
                    );

                if (user != null &&
                    !string.IsNullOrWhiteSpace(
                        user.Nickname))
                {
                    return user.Nickname;
                }
            }
            catch
            {
                // إذا تعذر جلب الاسم
                // نستخدم المعرّف.
            }

            return userId;
        }
    }

    // =============================================================
    // لعبة مزاج
    // =============================================================

    public class MazajGame
    {
        public List<Team> Teams { get; } =
            new();

        public List<Card> Cards { get; } =
            new();

        public List<string> TurnOrder { get; } =
            new();

        public bool Started { get; set; }

        public int CurrentPlayerIndex { get; set; }

        public int TurnVersion { get; set; }

        public string GroupId { get; set; } =
            "";

        public MazajGame()
        {
            // فريق الأمراء
            Teams.Add(
                new Team(
                    "احمر",
                    "🟥",
                    "الأمراء"
                )
            );

            // فريق النجوم
            Teams.Add(
                new Team(
                    "ازرق",
                    "🟦",
                    "النجوم"
                )
            );

            Cards =
                CreateCards();
        }

        public string CurrentPlayerId =>
            TurnOrder.Count == 0 ||
            CurrentPlayerIndex < 0 ||
            CurrentPlayerIndex >=
                TurnOrder.Count

                ? ""

                : TurnOrder[
                    CurrentPlayerIndex
                ];

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

                Team? team =
                    GetTeamByPlayer(
                        userId
                    );

                if (team != null &&
                    team.Players.TryGetValue(
                        userId,
                        out string? name))
                {
                    return name;
                }

                return userId;
            }
        }

        public Team? GetTeamByPlayer(
            string userId)
        {
            return Teams.FirstOrDefault(
                t => t.Players.ContainsKey(
                    userId
                )
            );
        }

        public bool AllCardsUsed =>
            Cards.All(
                c => c.Used
            );

        // =========================================================
        // إنشاء 65 بطاقة
        // 57 موجبة + 8 سالبة
        // =========================================================

        private static List<Card> CreateCards()
        {
            List<(string Name, int Value)> definitions =
                new()
                {
                    // -----------------------------
                    // البطاقات الموجبة
                    // -----------------------------

                    (
                        "ضربة الوحش محمد 🇮🇶❤️",
                        100
                    ),

                    (
                        "ضربة يوسف المهندس",
                        90
                    ),

                    (
                        "ضربة سرمد الوحش 🔥",
                        85
                    ),

                    (
                        "ضربة الأمراء",
                        80
                    ),

                    (
                        "ضربة النجوم",
                        78
                    ),

                    (
                        "ضربة الصقر",
                        76
                    ),

                    (
                        "ضربة الأسد",
                        74
                    ),

                    (
                        "ضربة البرق",
                        72
                    ),

                    (
                        "ضربة النار",
                        70
                    ),

                    (
                        "ضربة الرعد",
                        68
                    ),

                    (
                        "ضربة القناص",
                        66
                    ),

                    (
                        "ضربة المحترف",
                        64
                    ),

                    (
                        "ضربة البطل",
                        62
                    ),

                    (
                        "ضربة الملوك",
                        60
                    ),

                    (
                        "ضربة الذهب",
                        58
                    ),

                    (
                        "ضربة الفارس",
                        56
                    ),

                    (
                        "ضربة الذئب",
                        54
                    ),

                    (
                        "ضربة النسر",
                        52
                    ),

                    (
                        "ضربة الحظ",
                        50
                    ),

                    (
                        "ضربة المزاج",
                        48
                    ),

                    (
                        "ضربة قوية",
                        46
                    ),

                    (
                        "ضربة سريعة",
                        44
                    ),

                    (
                        "ضربة ذكية",
                        42
                    ),

                    (
                        "ضربة مفاجئة",
                        40
                    ),

                    (
                        "بطاقة الحظ",
                        38
                    ),

                    (
                        "بطاقة الفوز",
                        36
                    ),

                    (
                        "بطاقة القوة",
                        34
                    ),

                    (
                        "بطاقة النجم",
                        32
                    ),

                    (
                        "بطاقة الأمير",
                        30
                    ),

                    (
                        "بطاقة الشجاعة",
                        28
                    ),

                    (
                        "بطاقة التركيز",
                        26
                    ),

                    (
                        "بطاقة السرعة",
                        24
                    ),

                    (
                        "بطاقة المهارة",
                        22
                    ),

                    (
                        "بطاقة الذكاء",
                        20
                    ),

                    (
                        "بطاقة الصمود",
                        18
                    ),

                    (
                        "بطاقة التحدي",
                        16
                    ),

                    (
                        "بطاقة الفرصة",
                        14
                    ),

                    (
                        "بطاقة الأمل",
                        12
                    ),

                    (
                        "بطاقة البداية",
                        10
                    ),

                    (
                        "ضربة خفيفة",
                        8
                    ),

                    (
                        "مفاجأة مزاج",
                        6
                    ),

                    (
                        "حظك اليوم",
                        4
                    ),

                    (
                        "نقطة حظ",
                        2
                    ),

                    (
                        "ضربة إضافية",
                        15
                    ),

                    (
                        "ضربة ذهبية",
                        25
                    ),

                    (
                        "ضربة فضية",
                        35
                    ),

                    (
                        "ضربة ملكية",
                        45
                    ),

                    (
                        "ضربة أسطورية",
                        55
                    ),

                    (
                        "ضربة تاريخية",
                        65
                    ),

                    (
                        "ضربة صادمة",
                        75
                    ),

                    (
                        "ضربة القمة",
                        82
                    ),

                    (
                        "ضربة البطولة",
                        88
                    ),

                    (
                        "ضربة النهاية",
                        95
                    ),

                    (
                        "ضربة خاصة 1",
                        33
                    ),

                    (
                        "ضربة خاصة 2",
                        43
                    ),

                    (
                        "ضربة خاصة 3",
                        53
                    ),

                    (
                        "ضربة خاصة 4",
                        63
                    ),

                    // -----------------------------
                    // البطاقات السالبة
                    // -----------------------------

                    (
                        "هولو وئام الفگر",
                        -100
                    ),

                    (
                        "طاحج حضج توت 😂",
                        -90
                    ),

                    (
                        "صخام بوجهك ايهاب",
                        -80
                    ),

                    (
                        "سراوي تيتي لاتحل ولا تربط",
                        -70
                    ),

                    (
                        "هذا حظ زوز",
                        -60
                    ),

                    (
                        "لولو التعبانه",
                        -50
                    ),

                    (
                        "نواره السلبيه",
                        -40
                    ),

                    (
                        "بطاقة النحس",
                        -30
                    )
                };

            // =====================================================
            // تحقق من عدد البطاقات
            // =====================================================

            if (definitions.Count != 65)
            {
                throw new InvalidOperationException(
                    "يجب أن تكون البطاقات 65 بالضبط."
                );
            }

            // =====================================================
            // تحقق من 57 موجبة و8 سالبة
            // =====================================================

            if (
                definitions.Count(
                    x => x.Value > 0
                ) != 57 ||

                definitions.Count(
                    x => x.Value < 0
                ) != 8
            )
            {
                throw new InvalidOperationException(
                    "يجب أن تكون البطاقات 57 موجبة و8 سالبة."
                );
            }

            // =====================================================
            // لا توجد بطاقة موجبة أكثر من +100
            // =====================================================

            if (
                definitions
                    .Where(x => x.Value > 0)
                    .Any(x => x.Value > 100)
            )
            {
                throw new InvalidOperationException(
                    "لا توجد بطاقة موجبة أكبر من +100."
                );
            }

            return definitions
                .Select(
                    (x, index) =>
                        new Card(
                            index + 1,
                            x.Name,
                            x.Value
                        )
                )
                .ToList();
        }
    }

    // =============================================================
    // الفريق
    // =============================================================

    public class Team
    {
        public string Code { get; }

        public string Emoji { get; }

        public string Name { get; }

        public int Score { get; set; } =
            400;

        public Dictionary<string, string> Players { get; } =
            new();

        public Team(
            string code,
            string emoji,
            string name)
        {
            Code = code;
            Emoji = emoji;
            Name = name;
        }
    }

    // =============================================================
    // البطاقة
    // =============================================================

    public class Card
    {
        public int Number { get; }

        public string Name { get; }

        public int Value { get; }

        public bool Used { get; set; }

        public Card(
            int number,
            string name,
            int value)
        {
            Number = number;
            Name = name;
            Value = value;
        }
    }
}
