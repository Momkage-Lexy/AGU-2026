using Microsoft.Data.Sqlite;
using Microsoft.Web.WebView2.Core;
using System.Text;
using System.Text.Json;

namespace windows
{
    public partial class Form1 : Form
    {
        private readonly string dbPath = Path.Combine(Application.StartupPath, "survey.db");

        // ------------------------------------------------------------------
        // PRIZE WHEEL SETTINGS
        // These are only used the very first time the app runs (when survey.db
        // is created). After that, booth staff manage prizes from the admin panel.
        // ------------------------------------------------------------------
        private const string DefaultAdminPin = "1234";
        private const int DefaultPrizeQuantity = 25;
        private static readonly string[] DefaultPrizes =
            { "Beach Ball", "Giant Highlighter", "Calendar", "Sticker", "Magnet" };

        // Set when the correct PIN is entered; cleared when the admin panel closes.
        private bool adminUnlocked = false;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        // ------------------------------------------------------------------
        // SURVEY COLUMNS. One place to edit when the form changes.
        // Each entry: (database column, CSV header).
        // To add a field: add it here, add a property to SurveyResult below,
        // and add one line to GetValue(). Existing databases get the new
        // column added automatically on the next launch.
        // ------------------------------------------------------------------
        private static readonly (string Column, string Header)[] Fields =
        {
            ("submitted_at", "Submitted At"),
            ("role",         "Role"),
            ("experience",   "Experience"),
            ("brought",      "What brought you to the booth"),
            ("centers",      "Centers known before AGU"),
            ("follow_up",    "Willing to follow up"),
            ("first_name",   "First Name"),
            ("last_name",    "Last Name"),
            ("email",        "Email"),
            ("dob",          "Date of Birth"),
            ("grad_entry",   "Planned Grad School Entry"),
            ("program",      "Academic Program"),
            ("prize",        "Prize Won"),
        };

        public Form1()
        {
            InitializeComponent();
            InitializeDatabase();
            InitializeAsync();
        }

        // ==================================================================
        // DATABASE SETUP
        // ==================================================================

        private SqliteConnection OpenConnection()
        {
            var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();
            return conn;
        }

        private static int Exec(SqliteConnection conn, string sql, params (string Name, object? Value)[] args)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            foreach (var (name, value) in args)
                cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
            return cmd.ExecuteNonQuery();
        }

        private static object? Scalar(SqliteConnection conn, string sql, params (string Name, object? Value)[] args)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            foreach (var (name, value) in args)
                cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
            return cmd.ExecuteScalar();
        }

        private void InitializeDatabase()
        {
            using var conn = OpenConnection();
            InitializeResponsesTable(conn);
            InitializePrizeTables(conn);
        }

        private static void InitializeResponsesTable(SqliteConnection conn)
        {
            // 1. Create the table if this is a brand-new database.
            var cols = string.Join(",\n", Fields.Select(f => $"    {f.Column} TEXT"));
            Exec(conn,
                $"CREATE TABLE IF NOT EXISTS responses (\n" +
                $"    id INTEGER PRIMARY KEY AUTOINCREMENT,\n{cols}\n);");

            // 2. If the table already existed with fewer columns, add the missing ones.
            //    (CREATE TABLE IF NOT EXISTS never changes an existing table.)
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var info = conn.CreateCommand())
            {
                info.CommandText = "PRAGMA table_info(responses);";
                using var reader = info.ExecuteReader();
                while (reader.Read())
                    existing.Add(reader.GetString(1)); // column 1 = name
            }

            foreach (var f in Fields.Where(f => !existing.Contains(f.Column)))
                Exec(conn, $"ALTER TABLE responses ADD COLUMN {f.Column} TEXT;");
        }

        private static void InitializePrizeTables(SqliteConnection conn)
        {
            Exec(conn, @"
                CREATE TABLE IF NOT EXISTS prizes (
                    id        INTEGER PRIMARY KEY AUTOINCREMENT,
                    name      TEXT    NOT NULL,
                    remaining INTEGER NOT NULL DEFAULT 0,
                    given     INTEGER NOT NULL DEFAULT 0
                );
                CREATE TABLE IF NOT EXISTS settings (
                    key   TEXT PRIMARY KEY,
                    value TEXT NOT NULL
                );");

            Exec(conn, "INSERT OR IGNORE INTO settings (key, value) VALUES ('admin_pin', $pin);",
                ("$pin", DefaultAdminPin));

            // Add the starting prizes once. The flag stops them coming back
            // if staff later remove every prize on purpose.
            if (Scalar(conn, "SELECT value FROM settings WHERE key = 'prizes_seeded';") == null)
            {
                foreach (var name in DefaultPrizes)
                {
                    Exec(conn, "INSERT INTO prizes (name, remaining) VALUES ($name, $qty);",
                        ("$name", name), ("$qty", DefaultPrizeQuantity));
                }
                Exec(conn, "INSERT INTO settings (key, value) VALUES ('prizes_seeded', '1');");
            }
        }

        // ==================================================================
        // WEBVIEW + MESSAGES FROM THE PAGE
        // ==================================================================

        private async void InitializeAsync()
        {
            await webView21.EnsureCoreWebView2Async(null);

            // Attach before navigating so the page's first message isn't missed
            webView21.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

            string indexPath = Path.Combine(Application.StartupPath, "wwwroot", "index.html");
            webView21.CoreWebView2.Navigate(new Uri(indexPath).AbsoluteUri);
        }

        private void CoreWebView2_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                using var doc = JsonDocument.Parse(e.WebMessageAsJson);
                var msg = doc.RootElement;

                // Messages without a type come from older versions of the page: treat as a survey
                var type = GetString(msg, "type");
                if (type == "") type = "survey";

                switch (type)
                {
                    case "survey":
                        var data = msg.Deserialize<SurveyResult>(JsonOptions);
                        if (data != null)
                        {
                            SaveToDatabase(data);
                            ExportToCsv();
                        }
                        break;

                    case "getPrizes":
                        SendPrizes();
                        break;

                    case "prizeWon":
                        AwardPrize(GetLong(msg, "id"));
                        SendPrizes();
                        break;

                    case "adminLogin":
                        adminUnlocked = CheckPin(GetString(msg, "pin"));
                        Send(new { type = "adminLogin", ok = adminUnlocked });
                        break;

                    case "adminLogout":
                        adminUnlocked = false;
                        break;

                    case "savePrize":
                        RequireAdmin();
                        SavePrize(GetLong(msg, "id"), GetString(msg, "name"), GetLong(msg, "remaining"));
                        SendPrizes();
                        break;

                    case "deletePrize":
                        RequireAdmin();
                        DeletePrize(GetLong(msg, "id"));
                        SendPrizes();
                        break;

                    case "changePin":
                        RequireAdmin();
                        ChangePin(GetString(msg, "newPin"));
                        Send(new { type = "pinChanged", ok = true });
                        break;

                    case "closeApp":
                        RequireAdmin();
                        // Let this message handler finish before the window goes away
                        BeginInvoke(new Action(Close));
                        break;
                }
            }
            catch (Exception ex)
            {
                // Don't let one bad message crash the kiosk; log it next to the exe.
                File.AppendAllText(
                    Path.Combine(Application.StartupPath, "errors.log"),
                    $"{DateTime.Now:u} {ex}\n");
                Send(new { type = "error", message = ex.Message });
            }
        }

        private void Send(object message)
        {
            webView21.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(message, JsonOptions));
        }

        private static string GetString(JsonElement msg, string name) =>
            msg.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

        private static long GetLong(JsonElement msg, string name) =>
            msg.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : 0;

        // ==================================================================
        // PRIZES
        // ==================================================================

        public record Prize(long Id, string Name, long Remaining, long Given);

        private List<Prize> GetPrizes()
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, name, remaining, given FROM prizes ORDER BY id;";

            var list = new List<Prize>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                list.Add(new Prize(reader.GetInt64(0), reader.GetString(1), reader.GetInt64(2), reader.GetInt64(3)));
            return list;
        }

        private void SendPrizes() => Send(new { type = "prizes", prizes = GetPrizes() });

        private void AwardPrize(long id)
        {
            using var conn = OpenConnection();
            // "remaining > 0" means stock can never go negative
            Exec(conn,
                "UPDATE prizes SET remaining = remaining - 1, given = given + 1 WHERE id = $id AND remaining > 0;",
                ("$id", id));
        }

        private void SavePrize(long id, string name, long remaining)
        {
            name = name.Trim();
            if (name.Length == 0) throw new ArgumentException("The prize needs a name.");
            if (name.Length > 40) throw new ArgumentException("Prize names can be at most 40 characters.");
            if (remaining < 0) remaining = 0;

            using var conn = OpenConnection();

            var duplicate = Scalar(conn,
                "SELECT 1 FROM prizes WHERE name = $name COLLATE NOCASE AND id <> $id;",
                ("$name", name), ("$id", id));
            if (duplicate != null) throw new ArgumentException($"There's already a prize called {name}.");

            if (id > 0)
            {
                Exec(conn, "UPDATE prizes SET name = $name, remaining = $remaining WHERE id = $id;",
                    ("$name", name), ("$remaining", remaining), ("$id", id));
            }
            else
            {
                Exec(conn, "INSERT INTO prizes (name, remaining) VALUES ($name, $remaining);",
                    ("$name", name), ("$remaining", remaining));
            }
        }

        private void DeletePrize(long id)
        {
            using var conn = OpenConnection();
            Exec(conn, "DELETE FROM prizes WHERE id = $id;", ("$id", id));
        }

        // ==================================================================
        // ADMIN PIN
        // ==================================================================

        private void RequireAdmin()
        {
            if (!adminUnlocked) throw new UnauthorizedAccessException("Enter the admin PIN first.");
        }

        private bool CheckPin(string pin)
        {
            using var conn = OpenConnection();
            var stored = Scalar(conn, "SELECT value FROM settings WHERE key = 'admin_pin';") as string;
            return stored != null && pin == stored;
        }

        private void ChangePin(string newPin)
        {
            if (newPin.Length < 4 || newPin.Length > 8 || !newPin.All(char.IsDigit))
                throw new ArgumentException("The PIN must be 4 to 8 digits.");

            using var conn = OpenConnection();
            Exec(conn, "UPDATE settings SET value = $pin WHERE key = 'admin_pin';", ("$pin", newPin));
        }

        // ==================================================================
        // SURVEY RESPONSES
        // ==================================================================

        // Maps each database column to its value from the submitted form.
        private static string GetValue(SurveyResult r, string column) => column switch
        {
            "submitted_at" => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            "role"         => r.Role,
            "experience"   => r.Experience,
            "brought"      => r.Brought,
            "centers"      => string.Join("; ", r.Centers), // checkbox list -> one text cell
            "follow_up"    => r.FollowUp,
            "first_name"   => r.FirstName,
            "last_name"    => r.LastName,
            "email"        => r.Email,
            "dob"          => r.Dob,
            "grad_entry"   => r.GradEntry,
            "program"      => r.Program,
            "prize"        => r.Prize,
            _ => throw new InvalidOperationException($"No value mapped for column '{column}'")
        };

        private void SaveToDatabase(SurveyResult r)
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();

            var columns = string.Join(", ", Fields.Select(f => f.Column));
            var parameters = string.Join(", ", Fields.Select(f => "$" + f.Column));
            cmd.CommandText = $"INSERT INTO responses ({columns}) VALUES ({parameters});";

            foreach (var f in Fields)
                cmd.Parameters.AddWithValue("$" + f.Column, GetValue(r, f.Column) ?? "");

            cmd.ExecuteNonQuery();
        }

        private void ExportToCsv()
        {
            string finalPath = Path.Combine(Application.StartupPath, "survey_export.csv");
            string tempPath = Path.Combine(Application.StartupPath, "survey_export.tmp");

            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT {string.Join(", ", Fields.Select(f => f.Column))} FROM responses ORDER BY id;";

            using (var sw = new StreamWriter(tempPath, false, new UTF8Encoding(true))) // BOM so Excel reads accents
            {
                sw.WriteLine(string.Join(",", Fields.Select(f => Escape(f.Header))));

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var cells = new string[Fields.Length];
                    for (int i = 0; i < Fields.Length; i++)
                        cells[i] = Escape(reader.IsDBNull(i) ? "" : reader.GetString(i)); // old rows have NULLs in new columns
                    sw.WriteLine(string.Join(",", cells));
                }
            }

            File.Move(tempPath, finalPath, overwrite: true);
        }

        private static string Escape(string? s) =>
            "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";

        // Must match the survey object sent by sendToHost() in index.html.
        public class SurveyResult
        {
            public string Role { get; set; } = "";
            public string Experience { get; set; } = "";
            public string Brought { get; set; } = "";
            public List<string> Centers { get; set; } = new();
            public string FollowUp { get; set; } = "";
            public string FirstName { get; set; } = "";
            public string LastName { get; set; } = "";
            public string Email { get; set; } = "";
            public string Dob { get; set; } = "";
            public string GradEntry { get; set; } = "";
            public string Program { get; set; } = "";
            public string Prize { get; set; } = "";
        }
    }
}