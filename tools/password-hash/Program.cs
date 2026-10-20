using System.Text;

using Microsoft.AspNetCore.Identity;

Console.Error.Write("Password (hidden): ");
var password = new StringBuilder();
while (true)
{
    var key = Console.ReadKey(intercept: true);
    if (key.Key == ConsoleKey.Enter)
    {
        break;
    }

    if (key.Key == ConsoleKey.Backspace)
    {
        if (password.Length > 0)
        {
            password.Length--;
        }
    }
    else if (!char.IsControl(key.KeyChar))
    {
        password.Append(key.KeyChar);
    }
}

Console.Error.WriteLine();
if (password.Length < 12)
{
    Console.Error.WriteLine("Use at least 12 characters.");
    return 1;
}

Console.WriteLine(new PasswordHasher<object>().HashPassword(new(), password.ToString()));
password.Clear();
return 0;
