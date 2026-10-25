using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Text.Json;
using PixelArtDownscale;

namespace Pixelizator.Cli;

internal static class CleanDarkContoursCommand
{
    private const string Help = """
        Tessera clean-dark-contours — убрать короткие тёмные выступы на линиях.
        Использование: tessera clean-dark-contours input.png [-o output.png] [параметры]

          -o, --output PATH             PNG; по умолчанию <имя>_dark_contours.png.
          --dark-lightness N            Верхняя граница LAB L для тёмного пикселя: 0–100 (5).
          --max-dark-neighbors N        Максимум тёмных соседей из восьми: 0–8 (3).
          --max-cardinal-dark N         Максимум тёмных соседей по сторонам: 0–4 (2).
          --three-neighbor-dark5 N      Минимум тёмных пикселей в 5×5 при трёх соседях: 0–24 (9).
          --three-neighbor-component N  Минимальная доля главного контура: 0–100% (10).
          --three-neighbor-continuation N  Число продолжений линии для защиты детали: 0–8 (3; 0 выключает).
          --three-neighbor-protected-component N  Защищать продолжения на компонентах меньше 0–100% (20).
          --three-neighbor-protected-min N  Нижняя граница размера защищаемой компоненты: 0–100% (15).
          --repeated-component-similarity N  Сходство двух повторяющихся деталей: 0–100% (80).
          --two-cardinal-dark5 N        Минимум тёмных пикселей в 5×5 при двух прямых соседях: 0–24 (11).
          --two-cardinal-component N    Минимальная доля главного контура: 0–100% (40).
          --color-tolerance N           Допуск светлых цветов в LAB: 0–100 (28).
          --min-lightness-gain N        Минимальное увеличение LAB L: 0–100 (30).
          --alpha-clearance N           Отступ от прозрачности в пикселях: 1–15 (1).
          --min-bright-consensus N      Доля согласных светлых соседей: 0–100% (60).
          --cardinal-color-weight N     Вес цветов соседей по сторонам: 1–10 (3).
          --min-pair-extension N        Продолжение одинаковой пары через пиксель: 0–2 (1).
          --pair-color-tolerance N      Допуск цвета противоположной пары в LAB: 0–100 (8).
          --no-opposite-pair            Выключить восстановление по паре противоположных цветов.
          --passes N                    Число последовательных проходов: 1–10 (1).
          --overwrite                   Разрешить замену существующего результата.
          --json                        Отчёт JSON.
          -h, --help                    Эта справка.
        """;

    internal static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args is ["--help"] or ["-h"])
        {
            output.WriteLine(Help);
            return 0;
        }

        try
        {
            string? input = null, target = null;
            double darkness = 5, tolerance = 28, gain = 30, consensus = 60, component = 10,
                cardinalWeight = 3, pairTolerance = 8, twoCardinalComponent = 40,
                protectedComponent = 20, protectedMinimum = 15, repeatSimilarity = 80;
            int neighbors = 3, cardinal = 2, dark5 = 9, clearance = 1, passes = 1,
                pairExtension = 1, twoCardinalDark5 = 11, protectedContinuation = 3;
            bool overwrite = false, json = false, usePair = true;
            for (int q = 0; q < args.Length; q++)
            {
                string argument = args[q];
                int equals = argument.IndexOf('=');
                string name = equals < 0 ? argument : argument[..equals];
                string? inlineValue = equals < 0 ? null : argument[(equals + 1)..];
                string NextValue()
                {
                    if (inlineValue is not null) return inlineValue;
                    if (++q >= args.Length) throw new ArgumentException($"Нет значения для {name}.");
                    return args[q];
                }
                double NextDouble()
                {
                    if (!double.TryParse(NextValue(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                        throw new ArgumentException($"Некорректное число для {name}.");
                    return value;
                }
                int NextInt()
                {
                    if (!int.TryParse(NextValue(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                        throw new ArgumentException($"Некорректное целое число для {name}.");
                    return value;
                }
                switch (name)
                {
                    case "-o": case "--output": target = NextValue(); break;
                    case "--dark-lightness": darkness = NextDouble(); break;
                    case "--max-dark-neighbors": neighbors = NextInt(); break;
                    case "--max-cardinal-dark": cardinal = NextInt(); break;
                    case "--three-neighbor-dark5": dark5 = NextInt(); break;
                    case "--three-neighbor-component": component = NextDouble(); break;
                    case "--three-neighbor-continuation": protectedContinuation = NextInt(); break;
                    case "--three-neighbor-protected-component": protectedComponent = NextDouble(); break;
                    case "--three-neighbor-protected-min": protectedMinimum = NextDouble(); break;
                    case "--repeated-component-similarity": repeatSimilarity = NextDouble(); break;
                    case "--two-cardinal-dark5": twoCardinalDark5 = NextInt(); break;
                    case "--two-cardinal-component": twoCardinalComponent = NextDouble(); break;
                    case "--color-tolerance": tolerance = NextDouble(); break;
                    case "--min-lightness-gain": gain = NextDouble(); break;
                    case "--alpha-clearance": clearance = NextInt(); break;
                    case "--min-bright-consensus": consensus = NextDouble(); break;
                    case "--cardinal-color-weight": cardinalWeight = NextDouble(); break;
                    case "--min-pair-extension": pairExtension = NextInt(); break;
                    case "--pair-color-tolerance": pairTolerance = NextDouble(); break;
                    case "--no-opposite-pair": usePair = false; break;
                    case "--passes": passes = NextInt(); break;
                    case "--overwrite": overwrite = true; break;
                    case "--json": json = true; break;
                    default:
                        if (argument.StartsWith('-') || input is not null)
                            throw new ArgumentException($"Неизвестный аргумент: {argument}");
                        input = argument;
                        break;
                }
            }
            if (input is null) throw new ArgumentException("Не указан исходный PNG.");
            if (passes is < 1 or > 10) throw new ArgumentException("--passes должен быть от 1 до 10.");
            if (consensus is < 0 or > 100) throw new ArgumentException("--min-bright-consensus должен быть от 0 до 100.");
            if (component is < 0 or > 100) throw new ArgumentException("--three-neighbor-component должен быть от 0 до 100.");
            if (protectedContinuation is < 0 or > 8)
                throw new ArgumentException("--three-neighbor-continuation должен быть от 0 до 8.");
            if (protectedComponent is < 0 or > 100)
                throw new ArgumentException("--three-neighbor-protected-component должен быть от 0 до 100.");
            if (protectedMinimum < 0 || protectedMinimum > protectedComponent)
                throw new ArgumentException("--three-neighbor-protected-min должен быть от 0 до верхней границы.");
            if (repeatSimilarity is < 0 or > 100)
                throw new ArgumentException("--repeated-component-similarity должен быть от 0 до 100.");
            if (twoCardinalComponent is < 0 or > 100)
                throw new ArgumentException("--two-cardinal-component должен быть от 0 до 100.");
            var options = new DarkContourNoiseCleaner.Options(darkness, neighbors, tolerance, gain,
                clearance, consensus / 100, cardinal, dark5, component / 100, cardinalWeight,
                usePair, pairExtension, pairTolerance, twoCardinalDark5, twoCardinalComponent / 100,
                protectedContinuation, protectedComponent / 100, protectedMinimum / 100, repeatSimilarity / 100);
            string inputPath = Path.GetFullPath(input);
            string outputPath = Path.GetFullPath(target ?? Path.Combine(Path.GetDirectoryName(inputPath)!,
                Path.GetFileNameWithoutExtension(inputPath) + "_dark_contours.png"));
            if (!File.Exists(inputPath)) throw new IOException($"Исходный файл не найден: {inputPath}");
            if (string.Equals(inputPath, outputPath, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Путь результата должен отличаться от исходника.");
            if (Path.GetExtension(outputPath).ToLowerInvariant() != ".png")
                throw new ArgumentException("Результат должен быть PNG.");
            if (File.Exists(outputPath) && !overwrite)
                throw new IOException($"Результат уже существует: {outputPath}. Используйте --overwrite.");

            using var source = new Bitmap(inputPath);
            Bitmap current = source;
            int[] changed = new int[passes];
            try
            {
                for (int q = 0; q < passes; q++)
                {
                    var result = DarkContourNoiseCleaner.Clean(current, options);
                    if (!ReferenceEquals(current, source)) current.Dispose();
                    current = result.Image;
                    changed[q] = result.ChangedPixels;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                current.Save(outputPath, ImageFormat.Png);
            }
            finally
            {
                if (!ReferenceEquals(current, source)) current.Dispose();
            }
            if (json)
                output.WriteLine(JsonSerializer.Serialize(new { process = "clean-dark-contours", input = inputPath,
                    output = outputPath, width = source.Width, height = source.Height, changedPixels = changed.Sum(),
                    changedPerPass = changed, options }));
            else
                output.WriteLine($"Сохранено: {outputPath}; заменено пикселей: {changed.Sum()}.");
            return 0;
        }
        catch (ArgumentException exception)
        {
            error.WriteLine(exception.Message);
            return 2;
        }
        catch (Exception exception)
        {
            error.WriteLine(exception.Message);
            return 1;
        }
    }
}
