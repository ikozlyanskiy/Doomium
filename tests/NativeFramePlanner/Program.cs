using Doomium;

const int width = 320;
const int height = 200;
var palette = new NativePaletteColor[]
{
    new(0, 0, 0),
    new(240, 20, 20),
    new(20, 220, 20),
    new(20, 20, 240)
};

Check((x, y) => 1, expectedRectangles: 1);
Check((x, y) => (x < width / 2 ? 0 : 1) + (y < height / 2 ? 0 : 2),
    expectedRectangles: 4);

var random = new Random(17);
var noisy = MakeFrame((x, y) => random.Next(palette.Length));
var noisyPlan = NativeFramePlanner.Build(noisy, width, height, palette);
if (noisyPlan.Rectangles.Count > 640)
    throw new Exception($"Object budget exceeded: {noisyPlan.Rectangles.Count}");
CheckCoverage(noisyPlan);
Console.WriteLine($"Native frame planner passed: solid, quadrants, noisy frame ({noisyPlan.Columns}x{noisyPlan.Rows}, {noisyPlan.Rectangles.Count} fills).");

void Check(Func<int, int, int> colorAt, int expectedRectangles)
{
    var frame = MakeFrame(colorAt);
    var plan = NativeFramePlanner.Build(frame, width, height, palette);
    if (plan.Rectangles.Count != expectedRectangles)
        throw new Exception($"Expected {expectedRectangles} rectangles, got {plan.Rectangles.Count}.");
    CheckCoverage(plan);
    foreach (var rectangle in plan.Rectangles)
    {
        var x = (rectangle.Left + rectangle.Right) * width / (2 * plan.Columns);
        var y = (rectangle.Top + rectangle.Bottom) * height / (2 * plan.Rows);
        if (rectangle.Color != colorAt(x, y))
            throw new Exception("Native frame colors or orientation are incorrect.");
    }
}

byte[] MakeFrame(Func<int, int, int> colorAt)
{
    var frame = new byte[width * height * 4];
    for (var x = 0; x < width; x++)
    for (var y = 0; y < height; y++)
    {
        var color = palette[colorAt(x, y)];
        var offset = (x * height + y) * 4;
        frame[offset] = color.Red;
        frame[offset + 1] = color.Green;
        frame[offset + 2] = color.Blue;
        frame[offset + 3] = 255;
    }
    return frame;
}

void CheckCoverage(NativeFramePlan plan)
{
    var coverage = new byte[plan.Columns * plan.Rows];
    foreach (var rectangle in plan.Rectangles)
    {
        if (rectangle.Left < 0 || rectangle.Top < 0 || rectangle.Right > plan.Columns ||
            rectangle.Bottom > plan.Rows || rectangle.Left >= rectangle.Right ||
            rectangle.Top >= rectangle.Bottom || rectangle.Color >= palette.Length)
            throw new Exception("Invalid native fill rectangle.");
        for (var y = rectangle.Top; y < rectangle.Bottom; y++)
        for (var x = rectangle.Left; x < rectangle.Right; x++)
            coverage[y * plan.Columns + x]++;
    }
    if (coverage.Any(count => count != 1))
        throw new Exception("Native frame has uncovered or overlapping cells.");
}
