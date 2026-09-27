using System.ComponentModel.DataAnnotations;

namespace Capstone;

public class ProgressBar(int barLength, int total)
{
    // private static List<char> Idles = [ '⠇', '⠋', '⠙', '⠸', '⢰', '⠴', '⠦', '⡆' ];
    // private static List<char> Idles = [ '-', '\\', '|', '/' ];
    private static readonly List<string> Idles = [ "--", "\\/", "||", "/\\" ];
    private int _idleCounter;
    
    public void Update(int nCompleted)
    {
        float percentage = (float) nCompleted / total;
        if (percentage >= 1.0f) percentage = 1.0f;
        int filledChar = (int) (barLength * percentage);
        _idleCounter = (_idleCounter + 1) % Idles.Count;
        Console.Write($"\r[{new string('#', filledChar)}{new string('-', barLength - filledChar)}] ({(100.0f * percentage):00.00}%) {Idles[_idleCounter]} {nCompleted}/{total}");        
    }


}