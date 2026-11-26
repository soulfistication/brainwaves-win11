using System.Globalization;
using System.IO;
using System.Text;

namespace MatrixBlack;

public static class DemoEeg
{
    public static double Value(int channel, int index)
    {
        var t = index / EegStore.SampleRate;
        var freq = 3 + channel * 1.7;
        var amp = 18 + channel * 3;
        var value = amp * Math.Sin(2 * Math.PI * freq * t);
        value += 6 * Math.Sin(2 * Math.PI * (10 + channel) * t + channel);
        if (t >= 8 && t < 10)
            value += 80 * Math.Sin(2 * Math.PI * 40 * t);
        value += ((index * (channel + 3) * 17) % 100) / 100.0 * 4 - 2;
        return value;
    }

    public static void Write(string path, int seconds)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var sampleCount = (int)EegStore.SampleRate * seconds;
        var builder = new StringBuilder(sampleCount * 96);
        builder.Append("Counter,EEG 1,EEG 2,EEG 3,EEG 4,EEG 5,EEG 6,EEG 7,EEG 8,AccX,AccY,AccZ\n");

        for (var i = 0; i < sampleCount; i++)
        {
            builder.Append(i.ToString(CultureInfo.InvariantCulture));
            for (var channel = 0; channel < 8; channel++)
            {
                builder.Append(',');
                builder.Append(Value(channel, i).ToString("F3", CultureInfo.InvariantCulture));
            }

            builder.Append(",0,0,0\n");
        }

        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
