namespace BicepLocalExtension.Extensions;

public static class StringExtensions
{
    extension(string source)
    {
        public string ToCamelCase() 
        {
            if (string.IsNullOrWhiteSpace(source))
                return source;

            // Split by spaces, hyphens, underscores, or uppercase letters
            var words = System.Text.RegularExpressions.Regex
                .Split(source, @"[\s\-_]+|(?=[A-Z])")
                .Where(w => !string.IsNullOrEmpty(w))
                .ToArray();

            if (words.Length == 0)
                return source;

            // First word: lowercase
            var result = words[0].ToLowerInvariant();

            // Remaining words: capitalize first letter, lowercase the rest
            for (int i = 1; i < words.Length; i++)
            {
                var word = words[i];
                result += char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant();
            }

            return result;
        }
    }
}