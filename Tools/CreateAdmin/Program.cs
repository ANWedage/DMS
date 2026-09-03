using DMS.Data;
using DMS.Helpers;
using DMS.Models;
using MongoDB.Driver;

Console.WriteLine("DMS admin account creation");
Console.WriteLine("The password will not be displayed.");
Console.WriteLine();

var name = ReadRequired("Admin display name: ");
var username = SecurityValidator.NormalizeUsername(ReadRequired("Admin username: "));
if (!SecurityValidator.IsValidUsername(username))
{
    Console.Error.WriteLine("Username must be 3-20 characters and contain only letters, numbers, or underscores.");
    return 1;
}

var password = ReadPassword("Admin password: ");
if (!SecurityValidator.IsStrongPassword(password))
{
    Console.Error.WriteLine("Password must be at least 8 characters and include uppercase, lowercase, a number, and a symbol.");
    return 1;
}

var confirmation = ReadPassword("Confirm password: ");
if (!string.Equals(password, confirmation, StringComparison.Ordinal))
{
    Console.Error.WriteLine("Passwords do not match.");
    return 1;
}

try
{
    var context = new MongoDbContext();
    context.EnsureIndexes();

    if (context.Admins.Find(admin => admin.Username == username).Any())
    {
        Console.Error.WriteLine($"An admin with username '{username}' already exists.");
        return 1;
    }

    var (hash, salt) = PasswordHasher.HashPassword(password);
    context.Admins.InsertOne(new AdminUser
    {
        Name = name,
        Username = username,
        PasswordHash = hash,
        PasswordSalt = salt
    });

    Console.WriteLine($"Admin '{username}' was created successfully.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Admin creation failed: {ex.Message}");
    return 1;
}

static string ReadRequired(string prompt)
{
    while (true)
    {
        Console.Write(prompt);
        var value = Console.ReadLine()?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(value))
            return value;

        Console.WriteLine("A value is required.");
    }
}

static string ReadPassword(string prompt)
{
    Console.Write(prompt);
    var password = new System.Text.StringBuilder();

    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter)
            break;

        if (key.Key == ConsoleKey.Backspace)
        {
            if (password.Length > 0)
                password.Length--;
            continue;
        }

        if (!char.IsControl(key.KeyChar))
            password.Append(key.KeyChar);
    }

    Console.WriteLine();
    return password.ToString();
}
