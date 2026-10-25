using System.Globalization;

namespace TutorManagement.Modules.Academics.Domain.Teaching;

public static class StudentCode
{
    public static string Format(int number) =>
        "HS-" + number.ToString("D6", CultureInfo.InvariantCulture);
}
