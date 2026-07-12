using OpenCvSharp;

namespace Tsundosika.LimbusAssistant.Vision;

public sealed record ClashPanelLocation(
    PixelRect AllyPower,
    PixelRect EnemyPower,
    PixelRect AllyCoins,
    PixelRect EnemyCoins,
    PixelRect AllySanity,
    PixelRect AllySinIcon,
    PixelRect EnemySinIcon,
    PixelRect AllySkillIcon,
    PixelRect EnemySkillIcon,
    PixelRect WinRate,
    PixelRect CenterAnchor,
    double DetectionConfidence);

public static class ClashPanelDetector
{
    const double CenterSearchXMin = 0.35;
    const double CenterSearchXMax = 0.65;
    const double CenterSearchYMin = 0.25;
    const double CenterSearchYMax = 0.55;
    const int MinBrightPixelsForClash = 30;
    const double MinDetectionConfidence = 0.3;

    public static ClashPanelLocation? Detect(CaptureFrame frame, PixelRect content)
    {
        using var mat = FrameMat.ToMat(frame);
        var searchRect = BuildSearchRect(content);
        if (searchRect.Width < 2 || searchRect.Height < 2) return null;
        using var searchRegion = mat[new Rect(searchRect.X, searchRect.Y, searchRect.Width, searchRect.Height)];

        var centerX = FindCenterDivider(searchRegion, searchRect, content);
        if (centerX < 0)
        {
            return null;
        }

        var centerY = FindCenterY(searchRegion, searchRect, content);
        if (centerY < 0)
        {
            return null;
        }

        var anchor = new PixelRect(centerX - content.Width / 40, centerY - content.Height / 20,
            content.Width / 20, content.Height / 10);

        var confidence = MeasureClashConfidence(mat, content, centerX, centerY);
        if (confidence < MinDetectionConfidence)
        {
            return null;
        }

        return BuildLayout(content, centerX, centerY, anchor, confidence);
    }

    static PixelRect BuildSearchRect(PixelRect content)
    {
        var x = content.X + (int)(content.Width * CenterSearchXMin);
        var y = content.Y + (int)(content.Height * CenterSearchYMin);
        var w = (int)(content.Width * (CenterSearchXMax - CenterSearchXMin));
        var h = (int)(content.Height * (CenterSearchYMax - CenterSearchYMin));
        return new PixelRect(x, y, Math.Max(1, w), Math.Max(1, h));
    }

    static int FindCenterDivider(Mat searchRegion, PixelRect searchRect, PixelRect content)
    {
        using var hsv = new Mat();
        Cv2.CvtColor(searchRegion, hsv, ColorConversionCodes.BGR2HSV);

        using var brightMask = new Mat();
        Cv2.InRange(hsv, new Scalar(0, 0, 180), new Scalar(180, 80, 255), brightMask);

        using var warmMask = new Mat();
        Cv2.InRange(hsv, new Scalar(15, 100, 150), new Scalar(35, 255, 255), warmMask);

        using var combined = new Mat();
        Cv2.BitwiseOr(brightMask, warmMask, combined);

        var cols = new int[searchRegion.Cols];
        for (var y = 0; y < searchRegion.Rows; y++)
        {
            for (var x = 0; x < searchRegion.Cols; x++)
            {
                if (combined.At<byte>(y, x) > 0)
                {
                    cols[x]++;
                }
            }
        }

        var midCol = searchRegion.Cols / 2;
        var windowRadius = Math.Max(1, searchRegion.Cols / 20);
        var bestCol = -1;
        var bestDensity = 0;
        for (var c = midCol - windowRadius; c <= midCol + windowRadius; c++)
        {
            if (c < 0 || c >= searchRegion.Cols)
            {
                continue;
            }
            var density = 0;
            for (var dc = -2; dc <= 2; dc++)
            {
                var cc = c + dc;
                if (cc >= 0 && cc < searchRegion.Cols)
                {
                    density += cols[cc];
                }
            }
            if (density > bestDensity)
            {
                bestDensity = density;
                bestCol = c;
            }
        }

        if (bestDensity < MinBrightPixelsForClash)
        {
            return -1;
        }

        return searchRect.X + bestCol;
    }

    static int FindCenterY(Mat searchRegion, PixelRect searchRect, PixelRect content)
    {
        using var gray = new Mat();
        Cv2.CvtColor(searchRegion, gray, ColorConversionCodes.BGR2GRAY);

        var rows = new int[searchRegion.Rows];
        for (var y = 0; y < searchRegion.Rows; y++)
        {
            for (var x = 0; x < searchRegion.Cols; x++)
            {
                if (gray.At<byte>(y, x) > 180)
                {
                    rows[y]++;
                }
            }
        }

        var bestRow = searchRegion.Rows / 2;
        var bestCount = 0;
        var windowH = Math.Max(1, searchRegion.Rows / 8);
        for (var r = 0; r < searchRegion.Rows; r++)
        {
            var count = 0;
            for (var dr = -windowH; dr <= windowH; dr++)
            {
                var rr = r + dr;
                if (rr >= 0 && rr < searchRegion.Rows)
                {
                    count += rows[rr];
                }
            }
            if (count > bestCount)
            {
                bestCount = count;
                bestRow = r;
            }
        }

        return searchRect.Y + bestRow;
    }

    static double MeasureClashConfidence(Mat fullFrame, PixelRect content, int centerX, int centerY)
    {
        var signals = 0.0;
        var checks = 0.0;

        var leftPowerRect = BuildRect(content, centerX, centerY, -0.15, -0.02, 0.08, 0.07);
        var rightPowerRect = BuildRect(content, centerX, centerY, 0.07, -0.02, 0.08, 0.07);

        signals += MeasureTextPresence(fullFrame, leftPowerRect, content);
        checks += 1.0;
        signals += MeasureTextPresence(fullFrame, rightPowerRect, content);
        checks += 1.0;

        var leftCoinRect = BuildRect(content, centerX, centerY, -0.15, 0.06, 0.08, 0.04);
        var rightCoinRect = BuildRect(content, centerX, centerY, 0.07, 0.06, 0.08, 0.04);
        signals += MeasureTextPresence(fullFrame, leftCoinRect, content) * 0.5;
        checks += 0.5;
        signals += MeasureTextPresence(fullFrame, rightCoinRect, content) * 0.5;
        checks += 0.5;

        return checks > 0 ? signals / checks : 0;
    }

    static double MeasureTextPresence(Mat frame, PixelRect rect, PixelRect content)
    {
        var clamped = ClampToContent(rect, content);
        if (clamped.Width < 2 || clamped.Height < 2)
        {
            return 0;
        }
        if (clamped.X + clamped.Width > frame.Cols || clamped.Y + clamped.Height > frame.Rows)
        {
            return 0;
        }
        using var roi = frame[new Rect(clamped.X, clamped.Y, clamped.Width, clamped.Height)];
        using var gray = new Mat();
        Cv2.CvtColor(roi, gray, ColorConversionCodes.BGR2GRAY);
        var totalPixels = gray.Rows * gray.Cols;
        if (totalPixels == 0)
        {
            return 0;
        }
        var brightCount = Cv2.CountNonZero(gray.GreaterThan(160));
        var ratio = (double)brightCount / totalPixels;
        return Math.Clamp(ratio * 5.0, 0, 1);
    }

    static ClashPanelLocation BuildLayout(PixelRect content, int centerX, int centerY, PixelRect anchor, double confidence)
    {
        return new ClashPanelLocation(
            AllyPower: BuildRect(content, centerX, centerY, -0.15, -0.02, 0.08, 0.07),
            EnemyPower: BuildRect(content, centerX, centerY, 0.07, -0.02, 0.08, 0.07),
            AllyCoins: BuildRect(content, centerX, centerY, -0.15, 0.06, 0.08, 0.04),
            EnemyCoins: BuildRect(content, centerX, centerY, 0.07, 0.06, 0.08, 0.04),
            AllySanity: BuildRect(content, centerX, centerY, -0.21, 0.05, 0.055, 0.05),
            AllySinIcon: BuildRect(content, centerX, centerY, -0.17, -0.04, 0.04, 0.06),
            EnemySinIcon: BuildRect(content, centerX, centerY, 0.14, -0.04, 0.04, 0.06),
            AllySkillIcon: BuildRect(content, centerX, centerY, -0.25, -0.08, 0.09, 0.14),
            EnemySkillIcon: BuildRect(content, centerX, centerY, 0.17, -0.08, 0.09, 0.14),
            WinRate: BuildRect(content, centerX, centerY, -0.04, 0.02, 0.08, 0.05),
            CenterAnchor: anchor,
            DetectionConfidence: confidence);
    }

    static PixelRect BuildRect(PixelRect content, int centerX, int centerY, double relX, double relY, double relW, double relH)
    {
        var x = centerX + (int)(relX * content.Width);
        var y = centerY + (int)(relY * content.Height);
        var w = Math.Max(1, (int)(relW * content.Width));
        var h = Math.Max(1, (int)(relH * content.Height));
        return ClampToContent(new PixelRect(x, y, w, h), content);
    }

    static PixelRect ClampToContent(PixelRect rect, PixelRect content)
    {
        var x = Math.Max(content.X, Math.Min(rect.X, content.X + content.Width - 1));
        var y = Math.Max(content.Y, Math.Min(rect.Y, content.Y + content.Height - 1));
        var right = Math.Min(rect.X + rect.Width, content.X + content.Width);
        var bottom = Math.Min(rect.Y + rect.Height, content.Y + content.Height);
        return new PixelRect(x, y, Math.Max(1, right - x), Math.Max(1, bottom - y));
    }
}
