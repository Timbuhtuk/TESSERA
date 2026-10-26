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
          --no-tapered-run             Выключить поиск резко сужающейся чёрной полосы.
          --tapered-dark5-min N        Минимум тёмных соседей в 5×5: 0–24 (7).
          --tapered-dark5-max N        Максимум тёмных соседей в 5×5: 0–24 (10).
          --tapered-run-radius N       Радиус измерения длины полосы: 1–50 (20).
          --tapered-inside-length N    Минимальная длина внутренней полосы: 1–101 (8).
          --tapered-min-shrink N       Разница длин внешней и следующей внутренней полос: 1–100 (6).
          --tapered-bright N           Минимум повторов 3×3 со светлым центром: 0–10000 (4).
          --tapered-bright-ratio N     Светлых повторов к тёмным: 0–100 (1.5).
          --no-sparse-patch            Выключить повтор 3×3 при слабой тёмной поддержке.
          --sparse-patch-max-dark5 N   Максимум тёмных соседей в 5×5: 0–24 (5).
          --sparse-patch-max-cardinal N Максимум прямых тёмных соседей: 0–4 (1).
          --sparse-patch-bright N      Минимум повторов со светлым центром: 0–10000 (20).
          --sparse-patch-ratio N       Светлых повторов к тёмным: 0–100 (5).
          --no-secondary-sparse       Выключить расширение для второстепенных контуров.
          --secondary-sparse-dark5 N  Максимум тёмных соседей в 5×5: 0–24 (8).
          --secondary-sparse-component N  Максимальная доля главного контура: 0–100% (50).
          --no-soft-color-consensus   Выключить мягкое согласие близких оттенков.
          --soft-consensus N          Минимальное согласие: 0–100% (50).
          --soft-color-spread N       Максимальный разброс светлых соседей в LAB: 0–100 (40).
          --soft-max-dark-neighbors N Максимум тёмных соседей из восьми: 0–8 (2).
          --soft-max-dark5 N          Максимум тёмных соседей в 5×5: 0–24 (6).
          --no-coherent-gradient      Выключить допуск выступа по согласованному сильному градиенту.
          --coherent-dark5 N          Минимум тёмной поддержки в 5×5: 0–24 (10).
          --coherent-radius N         Радиус сравнения с похожими пикселями: 1–15 (5).
          --coherent-color-tolerance N  Допуск похожего цвета в LAB: 0–100 (24).
          --coherent-min-neighbors N  Минимум похожих соседей: 1–1000 (10).
          --coherent-magnitude-ratio N  Модуль центра к среднему: 0–10 (1.2).
          --coherent-min-agreement N  Согласие градиентов соседей: 0–100% (90).
          --coherent-min-alignment N  Совпадение центра с их градиентом: 0–100% (90).
          --no-divergent-gradient     Выключить допуск резкого выступа при расходящихся градиентах.
          --divergent-dark5 N         Тёмная поддержка в 5×5: 0–24 (7).
          --divergent-radius N        Радиус поиска похожих пикселей: 1–15 (5).
          --divergent-color-tolerance N  Допуск похожего цвета в LAB: 0–100 (24).
          --divergent-min-neighbors N Минимум похожих соседей: 1–1000 (10).
          --divergent-magnitude-ratio N  Модуль центра к среднему: 0–10 (1.4).
          --divergent-max-agreement N  Максимальное согласие градиентов соседей: 0–100% (15).
          --no-patch-gradient        Выключить совместную проверку окружения и градиента.
          --patch-gradient-dark5-min N  Нижняя граница поддержки 5×5: 0–24 (8).
          --patch-gradient-dark5-max N  Верхняя граница поддержки 5×5: 0–24 (10).
          --patch-gradient-bright N  Минимум светлых повторов 3×3: 0–10000 (6).
          --patch-gradient-ratio N   Светлых повторов к тёмным: 0–100 (3).
          --patch-gradient-radius N  Радиус поиска похожих цветов: 1–15 (5).
          --patch-gradient-color-tolerance N  Допуск цвета в LAB: 0–100 (24).
          --patch-gradient-min-neighbors N  Минимум похожих соседей: 1–1000 (10).
          --patch-gradient-magnitude-min N  Модуль к среднему, минимум: 0–10 (1.18).
          --patch-gradient-magnitude-max N  Модуль к среднему, максимум: 0–10 (1.4).
          --patch-gradient-max-agreement N  Максимальное согласие градиентов: 0–100% (50).
          --no-main-sparse            Выключить повтор окружения на основном контуре.
          --main-sparse-dark5 N       Максимальная поддержка 5×5: 0–24 (6).
          --main-sparse-bright N      Минимум светлых повторов 3×3: 0–10000 (8).
          --main-sparse-ratio N       Светлых повторов к тёмным: 0–100 (5).
          --main-sparse-gradient-radius N  Радиус градиентов: 1–15 (5).
          --main-sparse-color-tolerance N  Допуск цвета в LAB: 0–100 (24).
          --main-sparse-min-neighbors N  Минимум похожих соседей: 1–1000 (10).
          --main-sparse-max-magnitude N  Максимальный модуль к среднему: 0–10 (1).
          --no-bright-corner          Выключить продолжение светлого угла.
          --bright-corner-max-dark N  Максимум тёмных соседей: 0–8 (2).
          --bright-corner-max-cardinal N  Максимум прямых тёмных соседей: 0–4 (1).
          --bright-corner-dark5 N     Максимум тёмной поддержки 5×5: 0–24 (6).
          --bright-corner-component N  Максимальная доля главного контура: 0–100% (50).
          --bright-corner-run N       Минимальная длина светлых плеч: 1–15 (3).
          --bright-corner-color-tolerance N  Допуск цвета в LAB: 0–100 (8).
          --no-vertical-shade         Выключить выбор оттенка по вертикальной полосе.
          --vertical-medium-dark5 N  Поддержка 5×5 для близких оттенков: 0–24 (10).
          --vertical-medium-component-min N  Минимальная доля главного контура: 0–100% (25).
          --vertical-medium-component-max N  Максимальная доля главного контура: 0–100% (50).
          --vertical-medium-color-spread N  Максимальный разброс в LAB: 0–100 (30).
          --vertical-wide-dark5 N    Поддержка 5×5 для далёких цветов: 0–24 (11).
          --vertical-wide-component-min N  Минимальная доля главного контура: 0–100% (50).
          --vertical-wide-color-spread-min N  Минимальный разброс в LAB: 0–100 (50).
          --vertical-wide-color-spread-max N  Максимальный разброс в LAB: 0–100 (80).
          --vertical-run-radius N     Длина поиска вертикальной полосы: 1–15 (4).
          --vertical-color-tolerance N  Допуск цвета полосы в LAB: 0–100 (8).
          --conflict-radius N          Радиус выбора одного из соседних кандидатов: 0–4 (2).
          --require-common-gradient    Требовать противоположность общему градиенту похожих цветов.
          --common-gradient-radius N   Радиус поиска похожих цветов: 1–15 (5).
          --common-color-tolerance N   Максимальное расстояние цветов в LAB: 0–100 (24).
          --common-min-neighbors N     Минимум похожих соседей с градиентом: 1–1000 (5).
          --common-min-magnitude N     Минимальный модуль градиента центра: 0–100000 (0).
          --common-magnitude-ratio N   Модуль центра к среднему соседей: 0–10 (1.2).
          --common-min-agreement N     Согласие направлений соседей: 0–100% (20).
          --common-min-opposition N    Противоположность центра общему градиенту: 0–100% (80).
          --two-cardinal-dark5 N        Минимум тёмных пикселей в 5×5 при двух прямых соседях: 0–24 (11).
          --two-cardinal-component N    Минимальная доля главного контура: 0–100% (40).
          --two-cardinal-bright-pattern N  Минимум светлых повторов 3×3 для двух прямых тёмных соседей: 0–10000 (0).
          --no-color-patch            Выключить проверку цветовых повторов 3×3 (включена).
          --require-color-patch       Включить проверку цветовых повторов 3×3.
          --color-patch-tolerance N   RMS расстояние цветов окружения в LAB: 0–100 (15).
          --color-patch-min-bright N  Минимум повторов со светлым центром: 0–10000 (1).
          --color-patch-ratio N       Светлых повторов к тёмным: 0–100 (3).
          --color-patch-max-differences N  Допуск несовпадений в окружении 3×3: 0–2 (0).
          --color-patch-energy-rescue    Пропускать цветовой порог при сильном снижении перепада.
          --color-patch-energy-drop N    Минимальное снижение локального перепада: 0–100 % (80).
          --three-neighbor-energy-rescue Пропускать цветовой порог при 3 тёмных соседях, 1 прямом (включено).
          --no-three-neighbor-energy-rescue Выключить этот узкий пропуск цветового порога.
          --three-neighbor-energy-drop N Минимальное снижение перепада для этого случая: 0–100 % (40).
          --three-neighbor-distinct-colors N  Сколько цветов считать текстурой: 1–8 (4).
          --three-neighbor-flat-bright N      Минимальный вес светлых повторов на плоском участке: 0–10000 (2.1).
          --three-neighbor-textured-bright N  То же для текстуры: 0–10000 (0).
          --textured-gradient-rescue         Проверять выступы 2/1 по градиенту восстановленного цвета (включено).
          --no-textured-gradient-rescue      Выключить градиентную проверку этих выступов.
          --textured-gradient-colors N       Минимум разных цветов окружения: 1–8 (4).
          --textured-gradient-energy N       Минимальное снижение перепада: 0–100 % (58).
          --textured-gradient-bright N       Минимальный вес светлых повторов: 0–10000 (0.1).
          --textured-gradient-radius N       Радиус сравнения градиентов: 1–15 (3).
          --textured-gradient-tolerance N    Допуск похожих цветов LAB: 0–100 (24).
          --textured-gradient-neighbors N    Минимум похожих соседей: 1–1000 (5).
          --textured-gradient-opposition N   Минимальная противоположность: 0–100 % (20).
          --single-diagonal-gradient-rescue  Проверять одиночного диагонального соседа по цвету и градиенту (включено).
          --no-single-diagonal-gradient-rescue Выключить эту проверку.
          --single-diagonal-colors N          Минимум разных цветов окружения: 1–8 (4).
          --single-diagonal-lightness N       Максимальная светлота точки LAB L: 0–100 (2.5).
          --single-diagonal-energy N          Минимальное снижение перепада: 0–100 % (50).
          --single-diagonal-bright N          Минимальный вес светлых повторов: 0–10000 (0.1).
          --single-diagonal-radius N          Радиус сравнения градиентов: 1–15 (3).
          --single-diagonal-tolerance N       Допуск похожих цветов LAB: 0–100 (24).
          --single-diagonal-neighbors N       Минимум похожих соседей: 1–1000 (5).
          --single-diagonal-opposition N      Минимальная противоположность: 0–100 % (20).
          --color-patch-weighted-rescue  Дополнительно учитывать взвешенные цветовые повторы (включено).
          --no-color-patch-weighted-rescue Выключить взвешенные цветовые повторы.
          --weighted-patch-differences N Допуск несовпадений соседей: 0–2 (2).
          --weighted-patch-sigma N       Ширина цветового ядра LAB: >0–100 (5).
          --weighted-patch-min-bright N  Минимальный вес светлых повторов: 0–10000 (1).
          --weighted-patch-ratio N       Отношение весов светлых к тёмным: 0–100 (5).
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
                diagonalOpposition = 80, diagonalAgreement = 20, taperedBrightRatio = 1.5,
                sparsePatchRatio = 5, commonColorTolerance = 24,
                commonMinimumMagnitude = 0, commonMagnitudeRatio = 1.2,
                commonMinimumAgreement = 20, commonMinimumOpposition = 80,
                secondarySparseComponent = 50, softConsensus = 50, softColorSpread = 40,
                coherentColorTolerance = 24, coherentMagnitudeRatio = 1.2,
                coherentAgreement = 90, coherentAlignment = 90,
                divergentColorTolerance = 24, divergentMagnitudeRatio = 1.4,
                divergentMaximumAgreement = 15, patchGradientBrightRatio = 3,
                patchGradientColorTolerance = 24, patchGradientMagnitudeMinimum = 1.18,
                patchGradientMagnitudeMaximum = 1.4, patchGradientMaximumAgreement = 50,
                mainSparseBrightRatio = 5, mainSparseColorTolerance = 24,
                mainSparseMaximumMagnitude = 1, brightCornerComponent = 50,
                brightCornerColorTolerance = 8, verticalMediumComponentMin = 25,
                verticalMediumComponentMax = 50, verticalMediumColorSpread = 30,
                verticalWideComponentMin = 50, verticalWideColorSpreadMin = 50,
                verticalWideColorSpreadMax = 80, verticalColorTolerance = 8,
                colorPatchTolerance = 15, colorPatchRatio = 3, colorPatchEnergyDrop = 80,
                threeNeighborEnergyDrop = 40, threeNeighborFlatBright = 2.1,
                threeNeighborTexturedBright = 0, texturedGradientEnergy = 58,
                texturedGradientBright = 0.1, texturedGradientTolerance = 24,
                texturedGradientOpposition = 20,
                singleDiagonalLightness = 2.5, singleDiagonalEnergy = 50,
                singleDiagonalBright = 0.1,
                singleDiagonalTolerance = 24, singleDiagonalOpposition = 20,
                weightedPatchSigma = 5, weightedPatchMinBright = 1, weightedPatchRatio = 5;
            int neighbors = 3, cardinal = 2, dark5 = 9, clearance = 1, passes = 1,
                pairExtension = 1, twoCardinalDark5 = 11, twoCardinalBrightPattern = 0,
                protectedContinuation = 3,
                repeatedPatchRadius = 3, repeatedPatchDifferences = 2, largePatchRadius = 3,
                largePatchBright = 2, smallPatchRadius = 1, smallPatchBright = 10,
                minimumStraightRun = 8, straightRunRadius = 15, straightRunDark5 = 9,
                isolatedPatchRadius = 2, isolatedPatchBright = 3,
                diagonalBright = 7, diagonalGradientRadius = 3, diagonalSimilarNeighbors = 5,
                diagonalOppositionRadius = 5, smallPatchMaxCardinal = 1,
                taperedDark5Min = 7, taperedDark5Max = 10, taperedRadius = 20,
                taperedInsideLength = 8, taperedMinShrink = 6, taperedBright = 4,
                sparsePatchMaxDark5 = 5, sparsePatchMaxCardinal = 1, sparsePatchBright = 20,
                conflictRadius = 2, commonGradientRadius = 5, commonMinimumNeighbors = 5,
                secondarySparseDark5 = 8, softMaxDarkNeighbors = 2, softMaxDark5 = 6,
                coherentDark5 = 10, coherentRadius = 5, coherentMinimumNeighbors = 10,
                divergentDark5 = 7, divergentRadius = 5, divergentMinimumNeighbors = 10,
                patchGradientDark5Minimum = 8, patchGradientDark5Maximum = 10,
                patchGradientBright = 6, patchGradientRadius = 5, patchGradientMinimumNeighbors = 10,
                mainSparseDark5 = 6, mainSparseBright = 8, mainSparseGradientRadius = 5,
                mainSparseMinimumNeighbors = 10, brightCornerMaxDark = 2,
                brightCornerMaxCardinal = 1, brightCornerDark5 = 6, brightCornerRun = 3,
                verticalMediumDark5 = 10, verticalWideDark5 = 11, verticalRunRadius = 4,
                colorPatchMinBright = 1, colorPatchMaxDifferences = 0,
                weightedPatchDifferences = 2, threeNeighborDistinctColors = 4,
                texturedGradientColors = 4, texturedGradientRadius = 3,
                texturedGradientNeighbors = 5, singleDiagonalColors = 4,
                singleDiagonalRadius = 3, singleDiagonalNeighbors = 5;
            bool overwrite = false, json = false, usePair = true, allowDiagonalBridge = true,
                useStraightRun = true, useRepeatedMismatch = true, useDiagonalGradient = true,
                useDiagonalOpposition = true, useTaperedRun = true, useSparsePatch = true,
                requireCommonGradient = false, useSecondarySparse = true, useSoftColorConsensus = true,
                useCoherentGradient = true, useDivergentGradient = true, usePatchGradient = true,
                useMainSparse = true, useBrightCorner = true, useVerticalShade = true,
                requireColorPatch = true, colorPatchEnergyRescue = false,
                threeNeighborEnergyRescue = true, texturedGradientRescue = true,
                singleDiagonalGradientRescue = true,
                weightedPatchRescue = true;
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
                    case "--no-tapered-run": useTaperedRun = false; break;
                    case "--tapered-dark5-min": taperedDark5Min = NextInt(); break;
                    case "--tapered-dark5-max": taperedDark5Max = NextInt(); break;
                    case "--tapered-run-radius": taperedRadius = NextInt(); break;
                    case "--tapered-inside-length": taperedInsideLength = NextInt(); break;
                    case "--tapered-min-shrink": taperedMinShrink = NextInt(); break;
                    case "--tapered-bright": taperedBright = NextInt(); break;
                    case "--tapered-bright-ratio": taperedBrightRatio = NextDouble(); break;
                    case "--no-sparse-patch": useSparsePatch = false; break;
                    case "--sparse-patch-max-dark5": sparsePatchMaxDark5 = NextInt(); break;
                    case "--sparse-patch-max-cardinal": sparsePatchMaxCardinal = NextInt(); break;
                    case "--sparse-patch-bright": sparsePatchBright = NextInt(); break;
                    case "--sparse-patch-ratio": sparsePatchRatio = NextDouble(); break;
                    case "--no-secondary-sparse": useSecondarySparse = false; break;
                    case "--secondary-sparse-dark5": secondarySparseDark5 = NextInt(); break;
                    case "--secondary-sparse-component": secondarySparseComponent = NextDouble(); break;
                    case "--no-soft-color-consensus": useSoftColorConsensus = false; break;
                    case "--soft-consensus": softConsensus = NextDouble(); break;
                    case "--soft-color-spread": softColorSpread = NextDouble(); break;
                    case "--soft-max-dark-neighbors": softMaxDarkNeighbors = NextInt(); break;
                    case "--soft-max-dark5": softMaxDark5 = NextInt(); break;
                    case "--no-coherent-gradient": useCoherentGradient = false; break;
                    case "--coherent-dark5": coherentDark5 = NextInt(); break;
                    case "--coherent-radius": coherentRadius = NextInt(); break;
                    case "--coherent-color-tolerance": coherentColorTolerance = NextDouble(); break;
                    case "--coherent-min-neighbors": coherentMinimumNeighbors = NextInt(); break;
                    case "--coherent-magnitude-ratio": coherentMagnitudeRatio = NextDouble(); break;
                    case "--coherent-min-agreement": coherentAgreement = NextDouble(); break;
                    case "--coherent-min-alignment": coherentAlignment = NextDouble(); break;
                    case "--no-divergent-gradient": useDivergentGradient = false; break;
                    case "--divergent-dark5": divergentDark5 = NextInt(); break;
                    case "--divergent-radius": divergentRadius = NextInt(); break;
                    case "--divergent-color-tolerance": divergentColorTolerance = NextDouble(); break;
                    case "--divergent-min-neighbors": divergentMinimumNeighbors = NextInt(); break;
                    case "--divergent-magnitude-ratio": divergentMagnitudeRatio = NextDouble(); break;
                    case "--divergent-max-agreement": divergentMaximumAgreement = NextDouble(); break;
                    case "--no-patch-gradient": usePatchGradient = false; break;
                    case "--patch-gradient-dark5-min": patchGradientDark5Minimum = NextInt(); break;
                    case "--patch-gradient-dark5-max": patchGradientDark5Maximum = NextInt(); break;
                    case "--patch-gradient-bright": patchGradientBright = NextInt(); break;
                    case "--patch-gradient-ratio": patchGradientBrightRatio = NextDouble(); break;
                    case "--patch-gradient-radius": patchGradientRadius = NextInt(); break;
                    case "--patch-gradient-color-tolerance": patchGradientColorTolerance = NextDouble(); break;
                    case "--patch-gradient-min-neighbors": patchGradientMinimumNeighbors = NextInt(); break;
                    case "--patch-gradient-magnitude-min": patchGradientMagnitudeMinimum = NextDouble(); break;
                    case "--patch-gradient-magnitude-max": patchGradientMagnitudeMaximum = NextDouble(); break;
                    case "--patch-gradient-max-agreement": patchGradientMaximumAgreement = NextDouble(); break;
                    case "--no-main-sparse": useMainSparse = false; break;
                    case "--main-sparse-dark5": mainSparseDark5 = NextInt(); break;
                    case "--main-sparse-bright": mainSparseBright = NextInt(); break;
                    case "--main-sparse-ratio": mainSparseBrightRatio = NextDouble(); break;
                    case "--main-sparse-gradient-radius": mainSparseGradientRadius = NextInt(); break;
                    case "--main-sparse-color-tolerance": mainSparseColorTolerance = NextDouble(); break;
                    case "--main-sparse-min-neighbors": mainSparseMinimumNeighbors = NextInt(); break;
                    case "--main-sparse-max-magnitude": mainSparseMaximumMagnitude = NextDouble(); break;
                    case "--no-bright-corner": useBrightCorner = false; break;
                    case "--bright-corner-max-dark": brightCornerMaxDark = NextInt(); break;
                    case "--bright-corner-max-cardinal": brightCornerMaxCardinal = NextInt(); break;
                    case "--bright-corner-dark5": brightCornerDark5 = NextInt(); break;
                    case "--bright-corner-component": brightCornerComponent = NextDouble(); break;
                    case "--bright-corner-run": brightCornerRun = NextInt(); break;
                    case "--bright-corner-color-tolerance": brightCornerColorTolerance = NextDouble(); break;
                    case "--no-vertical-shade": useVerticalShade = false; break;
                    case "--vertical-medium-dark5": verticalMediumDark5 = NextInt(); break;
                    case "--vertical-medium-component-min": verticalMediumComponentMin = NextDouble(); break;
                    case "--vertical-medium-component-max": verticalMediumComponentMax = NextDouble(); break;
                    case "--vertical-medium-color-spread": verticalMediumColorSpread = NextDouble(); break;
                    case "--vertical-wide-dark5": verticalWideDark5 = NextInt(); break;
                    case "--vertical-wide-component-min": verticalWideComponentMin = NextDouble(); break;
                    case "--vertical-wide-color-spread-min": verticalWideColorSpreadMin = NextDouble(); break;
                    case "--vertical-wide-color-spread-max": verticalWideColorSpreadMax = NextDouble(); break;
                    case "--vertical-run-radius": verticalRunRadius = NextInt(); break;
                    case "--vertical-color-tolerance": verticalColorTolerance = NextDouble(); break;
                    case "--conflict-radius": conflictRadius = NextInt(); break;
                    case "--require-common-gradient": requireCommonGradient = true; break;
                    case "--common-gradient-radius": commonGradientRadius = NextInt(); break;
                    case "--common-color-tolerance": commonColorTolerance = NextDouble(); break;
                    case "--common-min-neighbors": commonMinimumNeighbors = NextInt(); break;
                    case "--common-min-magnitude": commonMinimumMagnitude = NextDouble(); break;
                    case "--common-magnitude-ratio": commonMagnitudeRatio = NextDouble(); break;
                    case "--common-min-agreement": commonMinimumAgreement = NextDouble(); break;
                    case "--common-min-opposition": commonMinimumOpposition = NextDouble(); break;
                    case "--two-cardinal-dark5": twoCardinalDark5 = NextInt(); break;
                    case "--two-cardinal-component": twoCardinalComponent = NextDouble(); break;
                    case "--two-cardinal-bright-pattern": twoCardinalBrightPattern = NextInt(); break;
                    case "--require-color-patch": requireColorPatch = true; break;
                    case "--no-color-patch": requireColorPatch = false; break;
                    case "--color-patch-tolerance": colorPatchTolerance = NextDouble(); break;
                    case "--color-patch-min-bright": colorPatchMinBright = NextInt(); break;
                    case "--color-patch-ratio": colorPatchRatio = NextDouble(); break;
                    case "--color-patch-max-differences": colorPatchMaxDifferences = NextInt(); break;
                    case "--color-patch-energy-rescue": colorPatchEnergyRescue = true; break;
                    case "--color-patch-energy-drop": colorPatchEnergyDrop = NextDouble(); break;
                    case "--three-neighbor-energy-rescue": threeNeighborEnergyRescue = true; break;
                    case "--no-three-neighbor-energy-rescue": threeNeighborEnergyRescue = false; break;
                    case "--three-neighbor-energy-drop": threeNeighborEnergyDrop = NextDouble(); break;
                    case "--three-neighbor-distinct-colors": threeNeighborDistinctColors = NextInt(); break;
                    case "--three-neighbor-flat-bright": threeNeighborFlatBright = NextDouble(); break;
                    case "--three-neighbor-textured-bright": threeNeighborTexturedBright = NextDouble(); break;
                    case "--textured-gradient-rescue": texturedGradientRescue = true; break;
                    case "--no-textured-gradient-rescue": texturedGradientRescue = false; break;
                    case "--textured-gradient-colors": texturedGradientColors = NextInt(); break;
                    case "--textured-gradient-energy": texturedGradientEnergy = NextDouble(); break;
                    case "--textured-gradient-bright": texturedGradientBright = NextDouble(); break;
                    case "--textured-gradient-radius": texturedGradientRadius = NextInt(); break;
                    case "--textured-gradient-tolerance": texturedGradientTolerance = NextDouble(); break;
                    case "--textured-gradient-neighbors": texturedGradientNeighbors = NextInt(); break;
                    case "--textured-gradient-opposition": texturedGradientOpposition = NextDouble(); break;
                    case "--single-diagonal-gradient-rescue": singleDiagonalGradientRescue = true; break;
                    case "--no-single-diagonal-gradient-rescue": singleDiagonalGradientRescue = false; break;
                    case "--single-diagonal-colors": singleDiagonalColors = NextInt(); break;
                    case "--single-diagonal-lightness": singleDiagonalLightness = NextDouble(); break;
                    case "--single-diagonal-energy": singleDiagonalEnergy = NextDouble(); break;
                    case "--single-diagonal-bright": singleDiagonalBright = NextDouble(); break;
                    case "--single-diagonal-radius": singleDiagonalRadius = NextInt(); break;
                    case "--single-diagonal-tolerance": singleDiagonalTolerance = NextDouble(); break;
                    case "--single-diagonal-neighbors": singleDiagonalNeighbors = NextInt(); break;
                    case "--single-diagonal-opposition": singleDiagonalOpposition = NextDouble(); break;
                    case "--color-patch-weighted-rescue": weightedPatchRescue = true; break;
                    case "--no-color-patch-weighted-rescue": weightedPatchRescue = false; break;
                    case "--weighted-patch-differences": weightedPatchDifferences = NextInt(); break;
                    case "--weighted-patch-sigma": weightedPatchSigma = NextDouble(); break;
                    case "--weighted-patch-min-bright": weightedPatchMinBright = NextDouble(); break;
                    case "--weighted-patch-ratio": weightedPatchRatio = NextDouble(); break;
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
            if (taperedDark5Min is < 0 or > 24 || taperedDark5Max is < 0 or > 24 ||
                taperedDark5Min > taperedDark5Max || taperedRadius is < 1 or > 50 ||
                taperedInsideLength is < 1 or > 101 || taperedMinShrink is < 1 or > 100 ||
                taperedBright is < 0 or > 10000 || taperedBrightRatio is < 0 or > 100)
                throw new ArgumentException("Некорректные параметры сужающейся полосы.");
            if (sparsePatchMaxDark5 is < 0 or > 24 || sparsePatchMaxCardinal is < 0 or > 4 ||
                sparsePatchBright is < 0 or > 10000 || sparsePatchRatio is < 0 or > 100)
                throw new ArgumentException("Некорректные параметры разреженного окружения.");
            if (secondarySparseDark5 is < 0 or > 24 || secondarySparseComponent is < 0 or > 100)
                throw new ArgumentException("Некорректные параметры второстепенного контура.");
            if (softConsensus is < 0 or > 100 || softColorSpread is < 0 or > 100 ||
                softMaxDarkNeighbors is < 0 or > 8 || softMaxDark5 is < 0 or > 24)
                throw new ArgumentException("Некорректные параметры мягкого согласия цветов.");
            if (coherentDark5 is < 0 or > 24 || coherentRadius is < 1 or > 15 ||
                coherentColorTolerance is < 0 or > 100 || coherentMinimumNeighbors is < 1 or > 1000 ||
                coherentMagnitudeRatio is < 0 or > 10 || coherentAgreement is < 0 or > 100 ||
                coherentAlignment is < 0 or > 100)
                throw new ArgumentException("Некорректные параметры согласованного градиента.");
            if (divergentDark5 is < 0 or > 24 || divergentRadius is < 1 or > 15 ||
                divergentColorTolerance is < 0 or > 100 || divergentMinimumNeighbors is < 1 or > 1000 ||
                divergentMagnitudeRatio is < 0 or > 10 || divergentMaximumAgreement is < 0 or > 100)
                throw new ArgumentException("Некорректные параметры расходящихся градиентов.");
            if (patchGradientDark5Minimum is < 0 or > 24 || patchGradientDark5Maximum is < 0 or > 24 ||
                patchGradientDark5Minimum > patchGradientDark5Maximum ||
                patchGradientBright is < 0 or > 10000 || patchGradientBrightRatio is < 0 or > 100 ||
                patchGradientRadius is < 1 or > 15 || patchGradientColorTolerance is < 0 or > 100 ||
                patchGradientMinimumNeighbors is < 1 or > 1000 ||
                patchGradientMagnitudeMinimum is < 0 or > 10 ||
                patchGradientMagnitudeMaximum < patchGradientMagnitudeMinimum ||
                patchGradientMagnitudeMaximum > 10 ||
                patchGradientMaximumAgreement is < 0 or > 100)
                throw new ArgumentException("Некорректные параметры окружения с градиентом.");
            if (mainSparseDark5 is < 0 or > 24 || mainSparseBright is < 0 or > 10000 ||
                mainSparseBrightRatio is < 0 or > 100 || mainSparseGradientRadius is < 1 or > 15 ||
                mainSparseColorTolerance is < 0 or > 100 || mainSparseMinimumNeighbors is < 1 or > 1000 ||
                mainSparseMaximumMagnitude is < 0 or > 10)
                throw new ArgumentException("Некорректные параметры основного контура.");
            if (brightCornerMaxDark is < 0 or > 8 || brightCornerMaxCardinal is < 0 or > 4 ||
                brightCornerDark5 is < 0 or > 24 || brightCornerComponent is < 0 or > 100 ||
                brightCornerRun is < 1 or > 15 || brightCornerColorTolerance is < 0 or > 100)
                throw new ArgumentException("Некорректные параметры светлого угла.");
            if (verticalMediumDark5 is < 0 or > 24 || verticalMediumComponentMin is < 0 or > 100 ||
                verticalMediumComponentMax < verticalMediumComponentMin ||
                verticalMediumComponentMax > 100 || verticalMediumColorSpread is < 0 or > 100 ||
                verticalWideDark5 is < 0 or > 24 || verticalWideComponentMin is < 0 or > 100 ||
                verticalWideColorSpreadMin is < 0 or > 100 ||
                verticalWideColorSpreadMax < verticalWideColorSpreadMin ||
                verticalWideColorSpreadMax > 100 || verticalRunRadius is < 1 or > 15 ||
                verticalColorTolerance is < 0 or > 100)
                throw new ArgumentException("Некорректные параметры вертикальной полосы.");
            if (conflictRadius is < 0 or > 4)
                throw new ArgumentException("--conflict-radius должен быть от 0 до 4.");
            if (commonGradientRadius is < 1 or > 15 || commonColorTolerance is < 0 or > 100 ||
                commonMinimumNeighbors is < 1 or > 1000 || commonMinimumMagnitude is < 0 or > 100000 ||
                commonMagnitudeRatio is < 0 or > 10 || commonMinimumAgreement is < 0 or > 100 ||
                commonMinimumOpposition is < 0 or > 100)
                throw new ArgumentException("Некорректные параметры общего градиента.");
            if (twoCardinalComponent is < 0 or > 100)
                throw new ArgumentException("--two-cardinal-component должен быть от 0 до 100.");
            if (twoCardinalBrightPattern is < 0 or > 10000)
                throw new ArgumentException("--two-cardinal-bright-pattern должен быть от 0 до 10000.");
            if (colorPatchTolerance is < 0 or > 100 || colorPatchMinBright is < 0 or > 10000 ||
                colorPatchRatio is < 0 or > 100)
                throw new ArgumentException("Некорректные параметры цветового повтора 3×3.");
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
                diagonalAgreement / 100, smallPatchMaxCardinal, useTaperedRun,
                taperedDark5Min, taperedDark5Max, taperedRadius, taperedInsideLength,
                taperedMinShrink, taperedBright, taperedBrightRatio,
                useSparsePatch, sparsePatchMaxDark5, sparsePatchMaxCardinal,
                sparsePatchBright, sparsePatchRatio, conflictRadius, requireCommonGradient,
                commonGradientRadius, commonColorTolerance, commonMinimumNeighbors,
                commonMinimumMagnitude, commonMagnitudeRatio, commonMinimumAgreement / 100,
                commonMinimumOpposition / 100, useSecondarySparse, secondarySparseDark5,
                secondarySparseComponent / 100, useSoftColorConsensus, softConsensus / 100,
                softColorSpread, softMaxDarkNeighbors, softMaxDark5,
                useCoherentGradient, coherentDark5, coherentRadius, coherentColorTolerance,
                coherentMinimumNeighbors, coherentMagnitudeRatio, coherentAgreement / 100,
                coherentAlignment / 100, useDivergentGradient, divergentDark5, divergentRadius,
                divergentColorTolerance, divergentMinimumNeighbors, divergentMagnitudeRatio,
                divergentMaximumAgreement / 100, usePatchGradient, patchGradientDark5Minimum,
                patchGradientDark5Maximum, patchGradientBright, patchGradientBrightRatio,
                patchGradientRadius, patchGradientColorTolerance, patchGradientMinimumNeighbors,
                patchGradientMagnitudeMinimum, patchGradientMagnitudeMaximum,
                patchGradientMaximumAgreement / 100) with
            {
                UseMainSparsePatch = useMainSparse,
                MainSparseMaximumDark5 = mainSparseDark5,
                MainSparseMinimumBrightMatches = mainSparseBright,
                MainSparseBrightRatio = mainSparseBrightRatio,
                MainSparseGradientRadius = mainSparseGradientRadius,
                MainSparseColorTolerance = mainSparseColorTolerance,
                MainSparseMinimumSimilarNeighbors = mainSparseMinimumNeighbors,
                MainSparseMaximumMagnitudeRatio = mainSparseMaximumMagnitude,
                UseBrightCornerContinuation = useBrightCorner,
                BrightCornerMaximumDarkNeighbors = brightCornerMaxDark,
                BrightCornerMaximumCardinalNeighbors = brightCornerMaxCardinal,
                BrightCornerMaximumDark5 = brightCornerDark5,
                BrightCornerMaximumComponentRatio = brightCornerComponent / 100,
                BrightCornerMinimumRun = brightCornerRun,
                BrightCornerColorTolerance = brightCornerColorTolerance,
                UseVerticalShadeContinuation = useVerticalShade,
                VerticalMediumDark5 = verticalMediumDark5,
                VerticalMediumMinimumComponentRatio = verticalMediumComponentMin / 100,
                VerticalMediumMaximumComponentRatio = verticalMediumComponentMax / 100,
                VerticalMediumMaximumColorSpread = verticalMediumColorSpread,
                VerticalWideDark5 = verticalWideDark5,
                VerticalWideMinimumComponentRatio = verticalWideComponentMin / 100,
                VerticalWideMinimumColorSpread = verticalWideColorSpreadMin,
                VerticalWideMaximumColorSpread = verticalWideColorSpreadMax,
                VerticalShadeSearchRadius = verticalRunRadius,
                VerticalShadeColorTolerance = verticalColorTolerance,
                TwoCardinalMinimumBrightPatternMatches = twoCardinalBrightPattern,
                RequireColorMatchedPatch = requireColorPatch,
                ColorMatchedPatchTolerance = colorPatchTolerance,
                ColorMatchedPatchMinimumBrightMatches = colorPatchMinBright,
                ColorMatchedPatchBrightRatio = colorPatchRatio,
                ColorMatchedPatchMaximumPatternDifferences = colorPatchMaxDifferences,
                UseColorPatchEnergyRescue = colorPatchEnergyRescue,
                ColorPatchEnergyRescueMinimumDrop = colorPatchEnergyDrop / 100,
                UseThreeNeighborEnergyRescue = threeNeighborEnergyRescue,
                ThreeNeighborEnergyRescueMinimumDrop = threeNeighborEnergyDrop / 100,
                ThreeNeighborEnergyDistinctColors = threeNeighborDistinctColors,
                ThreeNeighborEnergyFlatMinimumBrightWeight = threeNeighborFlatBright,
                ThreeNeighborEnergyTexturedMinimumBrightWeight = threeNeighborTexturedBright,
                UseTexturedGradientRescue = texturedGradientRescue,
                TexturedGradientMinimumDistinctColors = texturedGradientColors,
                TexturedGradientMinimumEnergyDrop = texturedGradientEnergy / 100,
                TexturedGradientMinimumBrightWeight = texturedGradientBright,
                TexturedGradientRadius = texturedGradientRadius,
                TexturedGradientColorTolerance = texturedGradientTolerance,
                TexturedGradientMinimumSimilarNeighbors = texturedGradientNeighbors,
                TexturedGradientMinimumOpposition = texturedGradientOpposition / 100,
                UseSingleDiagonalGradientRescue = singleDiagonalGradientRescue,
                SingleDiagonalMinimumDistinctColors = singleDiagonalColors,
                SingleDiagonalMaximumLightness = singleDiagonalLightness,
                SingleDiagonalMinimumEnergyDrop = singleDiagonalEnergy / 100,
                SingleDiagonalMinimumBrightWeight = singleDiagonalBright,
                SingleDiagonalGradientRadius = singleDiagonalRadius,
                SingleDiagonalColorTolerance = singleDiagonalTolerance,
                SingleDiagonalMinimumSimilarNeighbors = singleDiagonalNeighbors,
                SingleDiagonalMinimumOpposition = singleDiagonalOpposition / 100,
                UseWeightedColorPatchRescue = weightedPatchRescue,
                WeightedColorPatchMaximumPatternDifferences = weightedPatchDifferences,
                WeightedColorPatchSigma = weightedPatchSigma,
                WeightedColorPatchMinimumBrightWeight = weightedPatchMinBright,
                WeightedColorPatchBrightRatio = weightedPatchRatio
            };
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
