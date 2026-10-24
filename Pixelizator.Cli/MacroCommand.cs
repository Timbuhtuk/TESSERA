using System.Text.Json;
using PixelArtWorkflows;

namespace Pixelizator.Cli;

internal static class MacroCommand
{
    internal const string Help = """
        Tessera macro — список и применение многошаговых макросов обработки.

        Использование:
          tessera macro list --library PATH [--json]
          tessera macro apply INPUT --macro FILE -o OUTPUT [--overwrite] [--json]

        Команды:
          list       Прочитать корректные *.json из <library>/macros.
          apply      Выполнить все шаги в памяти и сохранить только итоговое изображение.

        Коды возврата: 0 — успех; 1 — ошибка файла, макроса или обработки; 2 — ошибка параметров.
        """;

    private const string ListHelp = """
        Использование: tessera macro list --library PATH [--json]

          --library PATH     Корень библиотеки Tessera; макросы читаются из папки macros.
          --json             JSON-отчёт, включая предупреждения загрузки.
          -h, --help         Эта справка.
        """;

    private const string ApplyHelp = """
        Использование: tessera macro apply INPUT --macro FILE -o OUTPUT [--overwrite] [--json]

          -i, --input PATH   Исходное изображение.
          --macro FILE       JSON-файл макроса.
          -o, --output PATH  Итоговое изображение PNG, JPG/JPEG или BMP.
          --overwrite        Разрешить замену существующего результата.
          --json             JSON-отчёт.
          -h, --help         Эта справка.
        """;

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length == 0 || args is ["--help"] or ["-h"])
        {
            output.WriteLine(Help);
            return 0;
        }
        return args[0] switch
        {
            "list" => List(args[1..], output, error),
            "apply" => Apply(args[1..], output, error),
            _ => Usage($"Неизвестная команда macro: {args[0]}.", error)
        };
    }

    private static int List(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length == 0 || args is ["--help"] or ["-h"])
        {
            output.WriteLine(ListHelp);
            return 0;
        }
        try
        {
            var parsed = Parse(args, ["--library"], ["--json"]);
            string library = Required(parsed, "--library");
            var store = new MacroStore(library);
            IReadOnlyList<MacroDefinition> macros = store.List();
            if (parsed.ContainsKey("--json"))
            {
                output.WriteLine(JsonSerializer.Serialize(new
                {
                    process = "macro-list",
                    library = Path.GetFullPath(library),
                    macroDirectory = store.DirectoryPath,
                    count = macros.Count,
                    warnings = store.LoadWarnings,
                    macros = macros.Select(macro => new
                    {
                        macro.Id,
                        macro.Name,
                        macro.Version,
                        stepCount = macro.Steps.Count,
                        operations = macro.Steps.Select(step => step.OperationId)
                    })
                }, WorkflowJson.Options));
            }
            else
            {
                foreach (MacroDefinition macro in macros)
                    output.WriteLine($"{macro.Id:D}  {macro.Name}  ({macro.Steps.Count} шагов)");
                foreach (string warning in store.LoadWarnings) error.WriteLine($"Предупреждение: {warning}");
            }
            return 0;
        }
        catch (CliUsageException ex) { error.WriteLine(ex.Message); return 2; }
        catch (Exception ex) { error.WriteLine(BaseMessage(ex)); return 1; }
    }

    private static int Apply(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length == 0 || args is ["--help"] or ["-h"])
        {
            output.WriteLine(ApplyHelp);
            return 0;
        }
        try
        {
            var parsed = Parse(args, ["--input", "--macro", "--output"], ["--overwrite", "--json"]);
            string input = Path.GetFullPath(Required(parsed, "--input"));
            string macroPath = Path.GetFullPath(Required(parsed, "--macro"));
            string target = Path.GetFullPath(Required(parsed, "--output"));
            bool overwrite = parsed.ContainsKey("--overwrite");
            if (string.Equals(input, target, StringComparison.OrdinalIgnoreCase))
                throw new CliUsageException("Исходник и результат должны иметь разные пути.");
            if (Path.GetExtension(target).ToLowerInvariant() is not (".png" or ".jpg" or ".jpeg" or ".bmp"))
                throw new CliUsageException("Результат должен быть PNG, JPG/JPEG или BMP.");
            if (!File.Exists(input)) throw new IOException($"Исходное изображение не найдено: {input}");
            if (!File.Exists(macroPath)) throw new IOException($"Файл макроса не найден: {macroPath}");
            if (Directory.Exists(target)) throw new IOException($"Вместо выходного файла указана папка: {target}");
            if (File.Exists(target) && !overwrite)
                throw new IOException($"Результат уже существует: {target}. Для замены добавьте --overwrite.");

            MacroDefinition macro = MacroStore.Read(macroPath);
            var recipe = new ProcessingRecipe { Version = macro.Version, Steps = macro.Steps };
            using var source = CliApplication.LoadImage(input);
            using var result = new WorkflowExecutor().Execute(source, recipe);
            CliApplication.SaveImage(result, target, overwrite);
            if (parsed.ContainsKey("--json"))
                output.WriteLine(JsonSerializer.Serialize(new
                {
                    process = "macro-apply",
                    input,
                    output = target,
                    macro = new { macro.Id, macro.Name, macro.Version, stepCount = macro.Steps.Count },
                    outputSize = new { width = result.Width, height = result.Height }
                }, WorkflowJson.Options));
            else output.WriteLine($"Макрос применён: {macro.Name}; сохранено: {target} ({result.Width}x{result.Height})");
            return 0;
        }
        catch (CliUsageException ex) { error.WriteLine(ex.Message); return 2; }
        catch (Exception ex) { error.WriteLine(BaseMessage(ex)); return 1; }
    }

    private static Dictionary<string, string> Parse(string[] args, IReadOnlyCollection<string> valueOptions,
        IReadOnlyCollection<string> flagOptions)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        bool positional = false;
        for (int q = 0; q < args.Length; q++)
        {
            string token = args[q];
            if (!positional && token == "--") { positional = true; continue; }
            if (positional || !token.StartsWith('-'))
            {
                Add("--input", token);
                continue;
            }

            int equals = token.IndexOf('=');
            string name = equals < 0 ? token : token[..equals];
            name = name switch { "-i" => "--input", "-o" => "--output", _ => name };
            if (flagOptions.Contains(name))
            {
                if (equals >= 0) throw new CliUsageException($"{name} не принимает значение.");
                Add(name, "true");
            }
            else if (valueOptions.Contains(name))
            {
                string value;
                if (equals >= 0) value = token[(equals + 1)..];
                else if (q + 1 < args.Length && !args[q + 1].StartsWith("--", StringComparison.Ordinal) &&
                         args[q + 1] is not "-i" and not "-o" and not "-h")
                    value = args[++q];
                else throw new CliUsageException($"Для {name} требуется значение.");
                Add(name, value);
            }
            else throw new CliUsageException($"Неизвестный параметр macro: {name}.");
        }
        return values;

        void Add(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new CliUsageException($"Пустое значение {name}.");
            if (!values.TryAdd(name, value)) throw new CliUsageException($"Параметр {name} указан несколько раз.");
        }
    }

    private static string Required(IReadOnlyDictionary<string, string> values, string name)
        => values.TryGetValue(name, out string? value) ? value : throw new CliUsageException($"Обязательный параметр: {name}.");

    private static int Usage(string message, TextWriter error)
    {
        error.WriteLine(message);
        return 2;
    }

    private static string BaseMessage(Exception exception)
        => exception is AggregateException ? exception.GetBaseException().Message : exception.Message;
}
