using System;
using System.Collections;
using System.IO;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace LethalCraft;

internal static class IntroCredits
{
    internal static void Install(Harmony harmony) => harmony.Patch(AccessTools.Method(typeof(InitializeGame),"Start"),postfix:new HarmonyMethod(typeof(IntroCredits),nameof(Attach)));
    static void Attach(InitializeGame __instance)
    {
        var template=UnityEngine.Object.FindObjectsOfType<TextMeshProUGUI>(true).FirstOrDefault(t=>t.text.Contains("Halden Electronics"));
        var canvas=template==null?null:template.GetComponentInParent<Canvas>();
        if(canvas==null)return;
        var credit=new GameObject("LethalCraftCredit",typeof(RectTransform),typeof(CanvasRenderer),typeof(TextMeshProUGUI));
        credit.transform.SetParent(canvas.transform,false);
        var rect=credit.GetComponent<RectTransform>();
        rect.anchorMin=rect.anchorMax=new Vector2(.5f,0);rect.pivot=new Vector2(.5f,0);
        rect.anchoredPosition=new Vector2(0,22);rect.sizeDelta=new Vector2(650,68);
        var text=credit.GetComponent<TextMeshProUGUI>();text.font=template!.font;text.fontSize=24;
        text.alignment=TextAlignmentOptions.Center;text.color=new Color(.5f,1f,.55f);text.raycastTarget=false;
        text.text="<size=60%>LETHALCRAFT</size>\nMade By ThatGuy";
        if(Array.IndexOf(Environment.GetCommandLineArgs(),"--lethalcraft-inspect-intro")>=0)credit.AddComponent<IntroCreditCapture>();
    }
}

internal sealed class IntroCreditCapture:MonoBehaviour
{
    IEnumerator Start()
    {
        string output=Environment.GetEnvironmentVariable("LETHALCRAFT_TEST_OUTPUT")??Path.Combine(BepInEx.Paths.PluginPath,"LethalCraft-test");
        Directory.CreateDirectory(output);
        yield return new WaitForSecondsRealtime(1);
        yield return new WaitForEndOfFrame();
        var text=GetComponent<TextMeshProUGUI>();
        File.WriteAllText(Path.Combine(output,"credit.txt"),"text="+text.text.Replace("\n"," / ")+"\nactive="+text.isActiveAndEnabled+"\n");
        ScreenCapture.CaptureScreenshot(Path.Combine(output,"intro-credit.png"));
    }
}
