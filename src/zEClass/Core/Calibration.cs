using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace zEClass.Core;

/// <summary>
/// Maps raw digitizer coordinates onto board surface coordinates.
///
/// An interactive whiteboard is rarely mounted pixel-aligned with its display, and the touch
/// controller reports its own coordinate frame. A three-point affine solve (translation,
/// scale, rotation, shear) is the minimum that corrects a misaligned or rotated panel;
/// a four-point capture adds a projective divide for panels viewed at an angle, which is the
/// normal case for a wall-mounted board shot slightly from the side.
/// </summary>
public sealed class Calibration
{
    public const int FileFormatVersion = 1;

    /// <summary>Number of targets shown during calibration.</summary>
    public const int TargetCount = 4;

    public Guid BoardId { get; set; } = Guid.Empty;

    /// <summary>Surface-space positions the operator aimed at, in board coordinates.</summary>
    public List<CalibrationPoint> Targets { get; set; } = new();

    /// <summary>Digitizer-space positions the operator actually hit.</summary>
    public List<CalibrationPoint> Captures { get; set; } = new();

    public DateTimeOffset CalibratedUtc { get; set; }

    public int DisplayWidth { get; set; }

    public int DisplayHeight { get; set; }

    public bool IsComplete =>
        Targets.Count == TargetCount && Captures.Count == TargetCount &&
        Captures.All(c => c.IsFinite);

    public Calibration Clone() => new()
    {
        BoardId = BoardId,
        Targets = Targets.Select(p => p.Clone()).ToList(),
        Captures = Captures.Select(p => p.Clone()).ToList(),
        CalibratedUtc = CalibratedUtc,
        DisplayWidth = DisplayWidth,
        DisplayHeight = DisplayHeight,
    };

    public static Calibration CreateForDisplay(int width, int height, double inset = 0.18)
    {
        var w = Math.Max(1, width);
        var h = Math.Max(1, height);
        var dx = w * inset;
        var dy = h * inset;

        // Corners in the standard 4-target order: top-left, top-right, bottom-right, bottom-left.
        var targets = new List<CalibrationPoint>
        {
            new(dx, dy),
            new(w - dx, dy),
            new(w - dx, h - dy),
            new(dx, h - dy),
        };
        return new Calibration { DisplayWidth = w, DisplayHeight = h, Targets = targets };
    }

    /// <summary>
    /// Digitizer-to-board transform for this capture, or null when it is incomplete or
    /// degenerate. Degenerate captures are routine in the field: hitting three targets in the
    /// same spot, or all four along a line, both make the projective solve singular.
    /// </summary>
    public CalibrationTransform? BuildTransform()
    {
        if (!IsComplete)
        {
            return null;
        }

        // Fit board -> digitizer with the standard 8-parameter solve, then invert.
        return ProjectiveSolver.SolveProjective(Targets, Captures)?.Invert();
    }

}

/// <summary>A calibration sample or target in one coordinate frame.</summary>
public sealed class CalibrationPoint
{
    public CalibrationPoint()
    {
    }

    public CalibrationPoint(double x, double y)
    {
        X = x;
        Y = y;
    }

    public double X { get; set; }

    public double Y { get; set; }

    /// <summary>
    /// True when both coordinates are real numbers. The JSON writer emits null for NaN, so a
    /// partially captured board reloads as NaN and is correctly reported as incomplete.
    /// </summary>
    public bool IsFinite => !double.IsNaN(X) && !double.IsNaN(Y) && !double.IsInfinity(X) &&
                            !double.IsInfinity(Y);

    public CalibrationPoint Clone() => new(X, Y);

    public override string ToString() => IsFinite ? $"({X:F1}, {Y:F1})" : "(unset)";
}

/// <summary>A 2D projective (homography) transform stored as its 3x3 matrix.</summary>
public sealed class CalibrationTransform
{
    private readonly double[] _m = new double[9];

    public CalibrationTransform()
    {
    }

    public CalibrationTransform(
        double a11, double a12, double a13,
        double a21, double a22, double a23,
        double a31, double a32, double a33)
    {
        _m[0] = a11;
        _m[1] = a12;
        _m[2] = a13;
        _m[3] = a21;
        _m[4] = a22;
        _m[5] = a23;
        _m[6] = a31;
        _m[7] = a32;
        _m[8] = a33;
    }

    public static CalibrationTransform Identity => new(1, 0, 0, 0, 1, 0, 0, 0, 1);

    public double this[int row, int col] => _m[(row * 3) + col];

    /// <summary>Determinant of the 3x3 matrix. Zero means the transform is not invertible.</summary>
    public double Determinant =>
        (_m[0] * ((_m[4] * _m[8]) - (_m[5] * _m[7]))) -
        (_m[1] * ((_m[3] * _m[8]) - (_m[5] * _m[6]))) +
        (_m[2] * ((_m[3] * _m[7]) - (_m[4] * _m[6])));

    public bool IsInvertible => Math.Abs(Determinant) > 1e-12;

    public CalibrationTransform? Invert()
    {
        var d = Determinant;
        if (Math.Abs(d) <= 1e-12)
        {
            return null;
        }

        var inv = new double[9];
        inv[0] = (_m[4] * _m[8]) - (_m[5] * _m[7]);
        inv[1] = -((_m[1] * _m[8]) - (_m[2] * _m[7]));
        inv[2] = (_m[1] * _m[5]) - (_m[2] * _m[4]);
        inv[3] = -((_m[3] * _m[8]) - (_m[5] * _m[6]));
        inv[4] = (_m[0] * _m[8]) - (_m[2] * _m[6]);
        inv[5] = -((_m[0] * _m[5]) - (_m[2] * _m[3]));
        inv[6] = (_m[3] * _m[7]) - (_m[4] * _m[6]);
        inv[7] = -((_m[0] * _m[7]) - (_m[1] * _m[6]));
        inv[8] = (_m[0] * _m[4]) - (_m[1] * _m[3]);

        for (var i = 0; i < 9; i++)
        {
            inv[i] /= d;
        }

        return new CalibrationTransform(
            inv[0], inv[1], inv[2],
            inv[3], inv[4], inv[5],
            inv[6], inv[7], inv[8]);
    }

    public double ApplyX(double x, double y)
    {
        var w = (_m[6] * x) + (_m[7] * y) + _m[8];
        if (Math.Abs(w) < 1e-12)
        {
            return double.NaN;
        }

        return ((_m[0] * x) + (_m[1] * y) + _m[2]) / w;
    }

    public double ApplyY(double x, double y)
    {
        var w = (_m[6] * x) + (_m[7] * y) + _m[8];
        if (Math.Abs(w) < 1e-12)
        {
            return double.NaN;
        }

        return (((_m[3] * x) + (_m[4] * y) + _m[5]) / w);
    }

    public CalibrationPoint Apply(CalibrationPoint p) => new(ApplyX(p.X, p.Y), ApplyY(p.X, p.Y));

    public bool IsIdentity =>
        Math.Abs(_m[0] - 1) < 1e-9 && Math.Abs(_m[1]) < 1e-9 && Math.Abs(_m[2]) < 1e-9 &&
        Math.Abs(_m[3]) < 1e-9 && Math.Abs(_m[4] - 1) < 1e-9 && Math.Abs(_m[5]) < 1e-9 &&
        Math.Abs(_m[6]) < 1e-9 && Math.Abs(_m[7]) < 1e-9 && Math.Abs(_m[8] - 1) < 1e-9;
}

/// <summary>Least-squares helper for the 4-point projective solve.</summary>
internal static class ProjectiveSolver
{
    /// <summary>
    /// Fits the 8-parameter homography that maps <paramref name="from"/> onto
    /// <paramref name="to"/>. Returns null when the system is singular, which happens when the
    /// operator hit three or four targets on a line, or double-tapped one target.
    /// </summary>
    public static CalibrationTransform? SolveProjective(
        IReadOnlyList<CalibrationPoint> from, IReadOnlyList<CalibrationPoint> to)
    {
        if (from.Count != to.Count || from.Count < 4)
        {
            return null;
        }

        var a = new double[8, 9];
        for (var i = 0; i < 4; i++)
        {
            var x = from[i].X;
            var y = from[i].Y;
            var u = to[i].X;
            var v = to[i].Y;

            a[i, 0] = x;
            a[i, 1] = y;
            a[i, 2] = 1;
            a[i, 3] = 0;
            a[i, 4] = 0;
            a[i, 5] = 0;
            a[i, 6] = -u * x;
            a[i, 7] = -u * y;
            a[i, 8] = u;

            a[i + 4, 0] = 0;
            a[i + 4, 1] = 0;
            a[i + 4, 2] = 0;
            a[i + 4, 3] = x;
            a[i + 4, 4] = y;
            a[i + 4, 5] = 1;
            a[i + 4, 6] = -v * x;
            a[i + 4, 7] = -v * y;
            a[i + 4, 8] = v;
        }

        var h = SolveLinearSystem(a);
        if (h is null)
        {
            return null;
        }

        return new CalibrationTransform(
            h[0], h[1], h[2],
            h[3], h[4], h[5],
            h[6], h[7], 1.0);
    }

    /// <summary>Gauss-Jordan elimination with partial pivoting. Returns null if singular.</summary>
    private static double[]? SolveLinearSystem(double[,] input)
    {
        var n = input.GetLength(0);
        var m = new double[n, n + 1];
        for (var r = 0; r < n; r++)
        {
            for (var c = 0; c < n; c++)
            {
                m[r, c] = input[r, c];
            }

            m[r, n] = input[r, n];
        }

        for (var col = 0; col < n; col++)
        {
            var pivot = col;
            for (var r = col + 1; r < n; r++)
            {
                if (Math.Abs(m[r, col]) > Math.Abs(m[pivot, col]))
                {
                    pivot = r;
                }
            }

            if (Math.Abs(m[pivot, col]) < 1e-12)
            {
                return null;
            }

            if (pivot != col)
            {
                for (var c = col; c <= n; c++)
                {
                    (m[col, c], m[pivot, c]) = (m[pivot, c], m[col, c]);
                }
            }

            var diag = m[col, col];
            for (var c = col; c <= n; c++)
            {
                m[col, c] /= diag;
            }

            for (var r = 0; r < n; r++)
            {
                if (r == col)
                {
                    continue;
                }

                var factor = m[r, col];
                if (factor == 0)
                {
                    continue;
                }

                for (var c = col; c <= n; c++)
                {
                    m[r, c] -= factor * m[col, c];
                }
            }
        }

        var result = new double[n];
        for (var i = 0; i < n; i++)
        {
            result[i] = m[i, n];
        }

        return result;
    }
}


/// <summary>
/// Persists calibration per board. The vendor software also stores calibration across
/// reinstalls, so this writes to the user's application data rather than the install folder.
/// </summary>
public sealed class CalibrationStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public CalibrationStore(string? root = null)
    {
        Root = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "zEClass", "calibration");
    }

    public string Root { get; }

    public string PathFor(Guid boardId) => Path.Combine(Root, $"{boardId:N}.json");

    public bool TryLoad(Guid boardId, out Calibration? calibration)
    {
        calibration = null;
        try
        {
            var path = PathFor(boardId);
            if (!File.Exists(path))
            {
                return false;
            }

            var doc = JsonSerializer.Deserialize<Calibration>(File.ReadAllText(path, Encoding.UTF8), Options);
            if (doc is null)
            {
                return false;
            }

            doc.BoardId = boardId;
            calibration = doc;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or JsonException or NotSupportedException
                                       or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Saves atomically and refuses to write an incomplete capture, so a half-finished
    /// calibration session can never replace a good one.
    /// </summary>
    public bool TrySave(Calibration calibration)
    {
        ArgumentNullException.ThrowIfNull(calibration);
        if (!calibration.IsComplete)
        {
            return false;
        }

        var path = PathFor(calibration.BoardId);
        var tmp = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Root);
            File.WriteAllText(tmp, JsonSerializer.Serialize(calibration, Options), new UTF8Encoding(false));
            File.Move(tmp, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or NotSupportedException or ArgumentException)
        {
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(tmp))
                {
                    File.Delete(tmp);
                }
            }
            catch (IOException)
            {
            }
        }
    }

    public bool Delete(Guid boardId)
    {
        try
        {
            var path = PathFor(boardId);
            if (!File.Exists(path))
            {
                return false;
            }

            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public IReadOnlyList<Guid> ListBoards()
    {
        try
        {
            if (!Directory.Exists(Root))
            {
                return Array.Empty<Guid>();
            }

            return Directory.EnumerateFiles(Root, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(n => !string.IsNullOrEmpty(n) && Guid.TryParseExact(n, "N", out _))
                .Select(n => Guid.ParseExact(n!, "N"))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<Guid>();
        }
    }
}
