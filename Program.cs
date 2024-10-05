using System;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;

class Program
{
    static string dbPath = "Data Source=ip_changes.db";
    static IConfiguration config;

    static void Main(string[] args)
    {
        // Load configuration from appsettings.json
        LoadConfiguration();

        Console.WriteLine("Checking for local IP changes...");

        // Ensure the database and table exist
        InitializeDatabase();

        // Get the current local IP address
        string currentIPAddress = GetLocalIPAddress();
        string previousIPAddress = GetPreviousIPAddress();

        if (currentIPAddress != previousIPAddress)
        {
            Console.WriteLine($"Local IP address changed from {previousIPAddress} to {currentIPAddress}");
            // Save the IP change in the database
            SaveIPChange(previousIPAddress, currentIPAddress);
            // Send an email notification about the IP change
            SendEmail(previousIPAddress, currentIPAddress);
        }
         else
        {
            Console.WriteLine("No change in local IP address.");
        }
    }

    // Function to load configuration
    static void LoadConfiguration()
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
        config = builder.Build();
    }

    // Function to get the current local IP address
    static string GetLocalIPAddress()
    {
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            var localIp = host.AddressList.FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork);
            return localIp?.ToString() ?? "No network adapters with an IPv4 address!";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching local IP: {ex.Message}");
            return null;
        }
    }

    // Function to initialize the SQLite database and table if they don't exist
    static void InitializeDatabase()
    {
        if (!File.Exists("ip_changes.db"))
        {
            SQLiteConnection.CreateFile("ip_changes.db");
        }

        using (var connection = new SQLiteConnection(dbPath))
        {
            connection.Open();
            string createTableQuery = @"
                CREATE TABLE IF NOT EXISTS IPChanges (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    OldIP TEXT,
                    NewIP TEXT,
                    ChangeDate TEXT
                )";
            using (var command = new SQLiteCommand(createTableQuery, connection))
            {
                command.ExecuteNonQuery();
            }
        }
    }

    // Function to get the most recent saved IP address from the SQLite database
    static string GetPreviousIPAddress()
    {
        using (var connection = new SQLiteConnection(dbPath))
        {
            connection.Open();
            string query = "SELECT NewIP FROM IPChanges ORDER BY Id DESC LIMIT 1";
            using (var command = new SQLiteCommand(query, connection))
            {
                object result = command.ExecuteScalar();
                return result?.ToString();
            }
        }
    }

    // Function to save IP change to the SQLite database
    static void SaveIPChange(string oldIP, string newIP)
    {
        using (var connection = new SQLiteConnection(dbPath))
        {
            connection.Open();
            string insertQuery = "INSERT INTO IPChanges (OldIP, NewIP, ChangeDate) VALUES (@oldIP, @newIP, @changeDate)";
            using (var command = new SQLiteCommand(insertQuery, connection))
            {
                command.Parameters.AddWithValue("@oldIP", oldIP ?? "None");
                command.Parameters.AddWithValue("@newIP", newIP);
                command.Parameters.AddWithValue("@changeDate", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                command.ExecuteNonQuery();
            }
        }
    }

    // Function to send email notification
    static void SendEmail(string oldIP, string newIP)
    {
        try
        {
            // Load email settings from the configuration
            var mailSettings = config.GetSection("MailSettings");
            string smtpHost = mailSettings["SmtpHost"];
            int smtpPort = int.Parse(mailSettings["SmtpPort"]);
            string smtpUser = mailSettings["SmtpUser"];
            string smtpPass = mailSettings["SmtpPass"];
            string fromAddress = mailSettings["FromAddress"];
            string[] toAddresses = mailSettings.GetSection("ToAddresses").Get<string[]>();
            if (toAddresses.Length == 0)
            {
                Console.WriteLine($"No recipients found. Skipping email.");
                return;
            }

            MailMessage mail = new MailMessage();
            mail.From = new MailAddress(fromAddress);
            foreach ( var t in toAddresses )
            {
                mail.To.Add(t);
            }
            mail.Subject = "Local IP Address Change Notification";
            mail.Body = $"The local IP address has changed from {oldIP} to {newIP}.";

            SmtpClient smtpClient = new SmtpClient(smtpHost, smtpPort)
            {
                Credentials = new NetworkCredential(smtpUser, smtpPass),
                EnableSsl = true
            };

            smtpClient.Send(mail);
            Console.WriteLine("Email notification sent.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending email: {ex.Message}");
        }
    }
}
