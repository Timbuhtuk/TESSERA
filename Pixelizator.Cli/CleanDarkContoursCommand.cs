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
          --repeated-patch-radius N    Радиус сравнения повторяющихся деталей: 0–4 (3; 0 выключает).
          --repeated-patch-differences N  Допустимое число разных соседей: 0–80 (2).
          --large-patch-radius N       Радиус поиска похожих окружений: 0–4 (3; 0 выключает).
          --large-patch-bright N       Минимум повторений со светлым центром: 0–10000 (2).
          --large-patch-ratio N        Светлых повторений к тёмным: 0–100 (1).
          --small-patch-radius N       Радиус для малых компонентов: 0–4 (1; 0 выключает).
          --small-patch-component N    Максимальный размер малой компоненты: 0–100% (6).
          --small-patch-max-cardinal N Максимум её тёмных прямых соседей: 0–4 (1).
          --small-patch-bright N       Минимум светлых повторений: 0–10000 (10).
          --small-patch-ratio N        Светлых повторений к тёмным: 0–100 (3).
          --no-diagonal-bridge         Сохранить тёмный пиксель между двумя диагональными областями.
          --diagonal-bridge-consensus N  Согласие светлых соседей такого пикселя: 0–100% (90).
          --no-straight-run            Выключить поиск выступа на прямой линии.
          --min-straight-run N         Минимальная длина прямой линии: 1–101 (8).
          --straight-run-radius N      Радиус поиска прямой линии: 1–50 (15).
          --straight-run-dark5 N       Поддержка 5×5 при двух тёмных соседях: 0–24 (9).
          --straight-run-consensus N   Согласие светлых соседей для выступа: 0–100% (50).
          --isolated-patch-radius N     Радиус повтора для одиночной точки: 0–4 (2; 0 выключает).
          --isolated-patch-bright N     Минимум светлых повторений: 0–10000 (3).
          --isolated-patch-ratio N      Светлых повторений к тёмным: 0–100 (2).
          --isolated-patch-consensus N  Согласие светлых соседей: 0–100% (50).
          --no-repeated-mismatch        Выключить восстановление по отличию повторяющихся деталей.
          --no-diagonal-gradient        Выключить проверку диагональной пары и градиента.
          --diagonal-color-tolerance N  Допуск цвета диагональной пары в LAB: 0–100 (8).
          --diagonal-bright N           Минимум светлых повторений окружения 3×3: 0–10000 (7).
          --diagonal-bright-ratio N     Светлых повторений к тёмным: 0–100 (2).
          --diagonal-gradient-ratio N   Модуль градиента точки к среднему соседей: 1–10 (1.22).
          --diagonal-gradient-radius N  Радиус сравнения градиентов: 1–15 (3).
          --diagonal-similar-color N    Допуск цвета похожих тёмных соседей в LAB: 0–100 (24).
          --diagonal-similar-neighbors N  Минимум похожих соседей: 1–1000 (5).
          --no-diagonal-opposition      Выключить сравнение направления градиентов.
          --diagonal-opposition-radius N  Радиус сравнения направлений: 1–15 (5).
          --diagonal-min-opposition N   Минимальная противоположность: 0–100% (80).
          --diagonal-min-agreement N    Согласие градиентов похожих соседей: 0–100% (20).
          --two-cardinal-dark5 N        Минимум тёмных пикселей в 5×5 при двух прямых соседях: 0–24 (11).
          --two-cardinal-component N    Минимальная доля главного контура: 0–100% (40).
          --color-tolerance N           Допуск светлых цветов в LAB: 0–100 (28).
          --min-lightness-gain N        Минимальное увеличение LAB L: 0–100 (20).
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
            double darkness = 5, tolerance = 28, gain = 20, consensus = 60, component = 10,
                cardinalWeight = 3, pairTolerance = 8, twoCardinalComponent = 40,
                protectedComponent = 20, protectedMinimum = 15, repeatSimilarity = 80,
                largePatchRatio = 1, smallPatchComponent = 6, smallPatchRatio = 3,
                diagonalBridgeConsensus = 90, straightRunConsensus = 50,
                isolatedPatchRatio = 2, isolatedPatchConsensus = 50,
                diagonalColorTolerance = 8, diagonalBrightRatio = 2,
                diagonalGradientRatio = 1.22, diagonalSimilarColor = 24,
                diagonalOpposition = 80, diagonalAgreement = 20;
            int neighbors = 3, cardinal = 2, dark5 = 9, clearance = 1, passes = 1,
                pairExtension = 1, twoCardinalDark5 = 11, protectedContinuation = 3,
                repeatedPatchRadius = 3, repeatedPatchDifferences = 2, largePatchRadius = 3,
                largePatchBright = 2, smallPatchRadius = 1, smallPatchBright = 10,
                minimumStraightRun = 8, straightRunRadius = 15, straightRunDark5 = 9,
                isolatedPatchRadius = 2, isolatedPatchBright = 3,
                diagonalBright = 7, diagonalGradientRadius = 3, diagonalSimilarNeighbors = 5,
                diagonalOppositionRadius = 5, smallPatchMaxCardinal = 1;
            bool overwrite = false, json = false, usePair = true, allowDiagonalBridge = true,
                useStraightRun = true, useRepeatedMismatch = true, useDiagonalGradient = true,
                useDiagonalOpposition = true;
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
                    case "--repeated-patch-radius": repeatedPatchRadius = NextInt(); break;
                    case "--repeated-patch-differences": repeatedPatchDifferences = NextInt(); break;
                    case "--large-patch-radius": largePatchRadius = NextInt(); break;
                    case "--large-patch-bright": largePatchBright = NextInt(); break;
                    case "--large-patch-ratio": largePatchRatio = NextDouble(); break;
                    case "--small-patch-radius": smallPatchRadius = NextInt(); break;
                    case "--small-patch-component": smallPatchComponent = NextDouble(); break;
                    case "--small-patch-max-cardinal": smallPatchMaxCardinal = NextInt(); break;
                    case "--small-patch-bright": smallPatchBright = NextInt(); break;
                    case "--small-patch-ratio": smallPatchRatio = NextDouble(); break;
                    case "--no-diagonal-bridge": allowDiagonalBridge = false; break;
                    case "--diagonal-bridge-consensus": diagonalBridgeConsensus = NextDouble(); break;
                    case "--no-straight-run": useStraightRun = false; break;
                    case "--min-straight-run": minimumStraightRun = NextInt(); break;
                    case "--straight-run-radius": straightRunRadius = NextInt(); break;
                    case "--straight-run-dark5": straightRunDark5 = NextInt(); break;
                    case "--straight-run-consensus": straightRunConsensus = NextDouble(); break;
                    case "--isolated-patch-radius": isolatedPatchRadius = NextInt(); break;
                    case "--isolated-patch-bright": isolatedPatchBright = NextInt(); break;
                    case "--isolated-patch-ratio": isolatedPatchRatio = NextDouble(); break;
                    case "--isolated-patch-consensus": isolatedPatchConsensus = NextDouble(); break;
                    case "--no-repeated-mismatch": useRepeatedMismatch = false; break;
                    case "--no-diagonal-gradient": useDiagonalGradient = false; break;
                    case "--diagonal-color-tolerance": diagonalColorTolerance = NextDouble(); break;
                    case "--diagonal-bright": diagonalBright = NextInt(); break;
                    case "--diagonal-bright-ratio": diagonalBrightRatio = NextDouble(); break;
                    case "--diagonal-gradient-ratio": diagonalGradientRatio = NextDouble(); break;
                    case "--diagonal-gradient-radius": diagonalGradientRadius = NextInt(); break;
                    case "--diagonal-similar-color": diagonalSimilarColor = NextDouble(); break;
                    case "--diagonal-similar-neighbors": diagonalSimilarNeighbors = NextInt(); break;
                    case "--no-diagonal-opposition": useDiagonalOpposition = false; break;
                    case "--diagonal-opposition-radius": diagonalOppositionRadius = NextInt(); break;
                    case "--diagonal-min-opposition": diagonalOpposition = NextDouble(); break;
                    case "--diagonal-min-agreement": diagonalAgreement = NextDouble(); break;
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
            if (repeatedPatchRadius is < 0 or > 4 || largePatchRadius is < 0 or > 4 ||
                smallPatchRadius is < 0 or > 4)
                throw new ArgumentException("Радиусы сопоставления должны быть от 0 до 4.");
            if (repeatedPatchDifferences is < 0 or > 80 || largePatchBright is < 0 or > 10000 ||
                smallPatchBright is < 0 or > 10000)
                throw new ArgumentException("Некорректный порог числа совпадений.");
            if (largePatchRatio is < 0 or > 100 || smallPatchRatio is < 0 or > 100 ||
                smallPatchComponent is < 0 or > 100 || smallPatchMaxCardinal is < 0 or > 4)
                throw new ArgumentException("Некорректный порог повторяющихся областей.");
            if (diagonalBridgeConsensus is < 0 or > 100)
                throw new ArgumentException("--diagonal-bridge-consensus должен быть от 0 до 100.");
            if (minimumStraightRun is < 1 or > 101 || straightRunRadius is < 1 or > 50 ||
                straightRunDark5 is < 0 or > 24 || straightRunConsensus is < 0 or > 100)
                throw new ArgumentException("Некорректные параметры прямой линии.");
            if (isolatedPatchRadius is < 0 or > 4 || isolatedPatchBright is < 0 or > 10000 ||
                isolatedPatchRatio is < 0 or > 100 || isolatedPatchConsensus is < 0 or > 100)
                throw new ArgumentException("Некорректные параметры одиночной точки.");
            if (diagonalColorTolerance is < 0 or > 100 || diagonalBright is < 0 or > 10000 ||
                diagonalBrightRatio is < 0 or > 100 || diagonalGradientRatio is < 1 or > 10 ||
                diagonalGradientRadius is < 1 or > 15 || diagonalSimilarColor is < 0 or > 100 ||
                diagonalSimilarNeighbors is < 1 or > 1000 || diagonalOppositionRadius is < 1 or > 15 ||
                diagonalOpposition is < 0 or > 100 || diagonalAgreement is < 0 or > 100)
                throw new ArgumentException("Некорректные параметры диагонального градиента.");
            if (twoCardinalComponent is < 0 or > 100)
                throw new ArgumentException("--two-cardinal-component должен быть от 0 до 100.");
            var options = new DarkContourNoiseCleaner.Options(darkness, neighbors, tolerance, gain,
                clearance, consensus / 100, cardinal, dark5, component / 100, cardinalWeight,
                usePair, pairExtension, pairTolerance, twoCardinalDark5, twoCardinalComponent / 100,
                protectedContinuation, protectedComponent / 100, protectedMinimum / 100, repeatSimilarity / 100,
                repeatedPatchRadius, repeatedPatchDifferences, largePatchRadius, largePatchBright,
                largePatchRatio, smallPatchRadius, smallPatchComponent / 100, smallPatchBright,
                smallPatchRatio, allowDiagonalBridge, diagonalBridgeConsensus / 100,
                useStraightRun, minimumStraightRun, straightRunRadius, straightRunDark5,
                straightRunConsensus / 100, isolatedPatchRadius, isolatedPatchBright,
                isolatedPatchRatio, isolatedPatchConsensus / 100, useRepeatedMismatch,
                useDiagonalGradient, diagonalColorTolerance, diagonalBright, diagonalBrightRatio,
                diagonalGradientRatio, diagonalGradientRadius, diagonalSimilarColor, diagonalSimilarNeighbors,
                useDiagonalOpposition, diagonalOppositionRadius, diagonalOpposition / 100,
                diagonalAgreement / 100, smallPatchMaxCardinal);
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
