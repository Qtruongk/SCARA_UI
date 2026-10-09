using System;
using System.Drawing;
using System.Windows.Forms;
using TsRemoteLib;

public class UITester {
    public static void Main() {
        TsPointS pt = new TsPointS();
        Console.WriteLine(pt.GetType().Name);
        ConfigS cfg = new ConfigS();
        Console.WriteLine(cfg.GetType().Name);
    }
}
