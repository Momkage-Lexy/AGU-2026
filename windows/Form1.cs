using Microsoft.Data.Sqlite;
using Microsoft.Web.WebView2.Core;
using System.Text;
using System.Text.Json;

namespace windows
{
    public partial class Form1 : Form
    {
        private readonly string dbPath = Path.Combine(Application.StartupPath, "survey.db");

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
        };

        public Form1()
        {
            InitializeComponent();
            InitializeDatabase();
            InitializeAsync();
        }

        private SqliteConnection OpenConnection()
        {
            var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();
            return conn;
        }

        private void InitializeDatabase()
        {
            using var conn = OpenConnection();

            // 1. Create the table if this is a brand-new database.
            using (var create = conn.CreateCommand())
            {
                var cols = string.Join(",\n", Fields.Select(f => $"    {f.Column} TEXT"));
                create.CommandText =
                    $"CREATE TABLE IF NOT EXISTS responses (\n" +
                    $"    id INTEGER PRIMARY KEY AUTOINCREMENT,\n{cols}\n);";
                create.ExecuteNonQuery();
            }

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
            {
                using var alter = conn.CreateCommand();
                alter.CommandText = $"ALTER TABLE responses ADD COLUMN {f.Column} TEXT;";
                alter.ExecuteNonQuery();
            }
        }

        private async void InitializeAsync()
        {
            await webView21.EnsureCoreWebView2Async(null);

            webView21.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

            string indexPath = Path.Combine(Application.StartupPath, "wwwroot", "index.html");
            webView21.CoreWebView2.Navigate(new Uri(indexPath).AbsoluteUri);
        }

        private void CoreWebView2_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var data = JsonSerializer.Deserialize<SurveyResult>(e.WebMessageAsJson, options);
                if (data == null) return;

                SaveToDatabase(data);
                ExportToCsv();
            }
            catch (Exception ex)
            {
                // Don't let one bad submission crash the kiosk; log it next to the exe.
                File.AppendAllText(
                    Path.Combine(Application.StartupPath, "errors.log"),
                    $"{DateTime.Now:u} {ex}\n");
            }
        }

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

        // Must match the object sent by postMessage() in index.html.
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
        }
    }
}