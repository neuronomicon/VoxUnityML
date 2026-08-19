
using System; 
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

// MonoBehaviour 상속을 지우고 static 클래스로 변경
public static class WindowsConsole
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeConsole();

    


    // 외부에서 부를 수 있도록 public static으로 변경
    public static void ShowConsole()
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR && !UNITY_SERVER
        AllocConsole();
        StreamWriter writer = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
        Console.SetOut(writer);

        Application.logMessageReceived += HandleLog;
        
        Console.WriteLine("======================================");
        Console.WriteLine("  Unity 실시간 디버그 터미널 활성화됨  ");
        Console.WriteLine("======================================");
#endif
    }

    // static 함수로 변경
    private static void HandleLog(string logString, string stackTrace, LogType type)
    {
        Console.WriteLine($"[{type}] {logString}");
        //Console.WriteLine(logString);
    }

    // 게임 종료 시 호출할 해제 함수 생성
    public static void HideConsole()
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR && !UNITY_SERVER
        Application.logMessageReceived -= HandleLog;
        FreeConsole();
#endif
    }
}