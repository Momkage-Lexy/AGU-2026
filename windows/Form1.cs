using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using System.Windows.Forms;


namespace windows {

    public partial class Form1 : Form
    {
        private string dbPath = Path.Combine(Application.StartupPath, "survey.db");

        public Form1()
        {
            InitializeComponent();
            InitializeDatabase();
            InitializeAsync();
        }

        private void InitializeDatabase()
        {
            // Connect to DB
            if (!File.Exists(dbPath))
            {
                using var connection = new SqliteConnection($"Data Source={dbPath}");
                connection.Open();
            }

            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();

            // Run up script
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS responses (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    role TEXT,
                    experience TEXT,
                    brought TEXT,
                    email TEXT,
                );
            ";

            // Get # of entries
            cmd.ExecuteNonQuery();
        }

        private async void InitializeAsync()
        {
            // Initialize Web View 2
            await webView21.EnsureCoreWebView2Async(null);

            // Get UI 
            string indexPath = Path.Combine(Application.StartupPath, "wwwroot", "index.html");
            webView21.CoreWebView2.Navigate($"file:///{indexPath.Replace("\\", "/")}");

            // Send UI to view
            webView21.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
        }

        private void CoreWebView2_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            // Transform user input to json & save to db
            var json = e.WebMessageAsJson;
            var data = JsonSerializer.Deserialize<SurveyResult>(json);
            SaveToDatabase(data);
        }

        private void SaveToDatabase(SurveyResult r)
        {
            // Connect to DB
            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();

            // Insert user data
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO responses (role, experience, brought, email)
                VALUES ($ts, $n, $o, $e)
            ";

            cmd.Parameters.AddWithValue("$ts", r.role);
            cmd.Parameters.AddWithValue("$n", r.experience);
            cmd.Parameters.AddWithValue("$o", r.brought);
            cmd.Parameters.AddWithValue("$e", r.email);

            // Get number of entries
            cmd.ExecuteNonQuery();
            ExportToCsv();
        }

        public class SurveyResult
        {
            public string role { get; set; }
            public string experience { get; set; }
            public string brought { get; set; }
            public string email { get; set; }
        }

        private void ExportToCsv()
        {
            string finalPath = Path.Combine(Application.StartupPath, "survey_export.csv");
            string tempPath = Path.Combine(Application.StartupPath, "survey_export.tmp");

            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT role, experience, brought, email FROM responses";

            // Write to a temporary file first
            using (var sw = new StreamWriter(tempPath, false))
            {
                sw.WriteLine("Role,Experience,What brought you to the booth,Email"); // Header

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    sw.WriteLine(
                        $"{Escape(reader.GetString(0))}," +
                        $"{Escape(reader.GetString(1))}," +
                        $"{Escape(reader.GetString(2))}," +
                        $"{Escape(reader.GetString(3))}"
                    );
                }
            }

            // Replace the old CSV atomically
            if (File.Exists(finalPath))
            {
                File.Delete(finalPath);  // Remove old file
            }

            File.Move(tempPath, finalPath);  // Rename temp → final
        }
        private string Escape(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "\"\"";

            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

    }
}