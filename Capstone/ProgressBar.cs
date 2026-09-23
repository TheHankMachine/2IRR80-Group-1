using System.ComponentModel.DataAnnotations;

namespace Capstone;

public class ProgressBar(int barLength, int total)
{
    
    public int BarLength { get; private set; } = barLength;

    // private static List<char> Idles = [ '⠇', '⠋', '⠙', '⠸', '⢰', '⠴', '⠦', '⡆' ];
    // private static List<char> Idles = [ '-', '\\', '|', '/' ];
    private static List<string> Idles = [ "--", "\\/", "||", "/\\" ];
    private int IdleCounter;
    
    public void Update(int n)
    {
        float p = (float) n / total;
        int b = (int) (barLength * p);
        IdleCounter = (IdleCounter + 1) % Idles.Count;
        Console.Write($"\r[{new string('#', b)}{new string('-', barLength - b)}] ({(100.0f * p).ToString("00.00")}%) {Idles[IdleCounter]} {n}/{total}");        
    }


}