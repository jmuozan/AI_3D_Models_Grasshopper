using System;
using System.IO;
using System.Text;
using SD = System.Drawing;

using Rhino;
using Grasshopper.Kernel;

namespace RhinoCodePlatform.Rhino3D.Projects.Plugin.GH
{
  public sealed class AssemblyInfo : GH_AssemblyInfo
  {
    static readonly string s_assemblyIconData = "[[ASSEMBLY-ICON]]";
    static readonly string s_categoryIconData = "[[ASSEMBLY-CATEGORY-ICON]]";

    public static readonly SD.Bitmap PluginIcon = default;
    public static readonly SD.Bitmap PluginCategoryIcon = default;

    static AssemblyInfo()
    {
      if (!s_assemblyIconData.Contains("ASSEMBLY-ICON"))
      {
        using (var aicon = new MemoryStream(Convert.FromBase64String(s_assemblyIconData)))
          PluginIcon = new SD.Bitmap(aicon);
      }

      if (!s_categoryIconData.Contains("ASSEMBLY-CATEGORY-ICON"))
      {
        using (var cicon = new MemoryStream(Convert.FromBase64String(s_categoryIconData)))
          PluginCategoryIcon = new SD.Bitmap(cicon);
      }
    }

    public override Guid Id { get; } = new Guid("11d238a3-2ff7-4f42-bf4e-da1fee9a63fa");

    public override string AssemblyName { get; } = "AI_Tools.Components";
    public override string AssemblyVersion { get; } = "0.1.31215.9062";
    public override string AssemblyDescription { get; } = "Generate Breps in Grasshopper with prompts (Using ZOO's Text to CAD model) or from images (Using OpenAI's vision model)";
    public override string AuthorName { get; } = "Jorge Muñoz Zanón";
    public override string AuthorContact { get; } = "jmuozan@gmail.com";
    public override GH_LibraryLicense AssemblyLicense { get; } = GH_LibraryLicense.unset;
    public override SD.Bitmap AssemblyIcon { get; } = PluginIcon;
  }

  public class ProjectComponentPlugin : GH_AssemblyPriority
  {
    static readonly Guid s_projectId = new Guid("11d238a3-2ff7-4f42-bf4e-da1fee9a63fa");
    static readonly string s_projectData = "ewogICJob3N0IjogewogICAgIm5hbWUiOiAiUmhpbm8zRCIsCiAgICAidmVyc2lvbiI6ICI4LjEyLjI0MjgyXHUwMDJCNzAwMiIsCiAgICAib3MiOiAibWFjT1MiLAogICAgImFyY2giOiAiYXJtNjQiCiAgfSwKICAiaWQiOiAiMTFkMjM4YTMtMmZmNy00ZjQyLWJmNGUtZGExZmVlOWE2M2ZhIiwKICAiaWRlbnRpdHkiOiB7CiAgICAibmFtZSI6ICJBSV9Ub29scyIsCiAgICAidmVyc2lvbiI6ICIwLjEtYmV0YSIsCiAgICAicHVibGlzaGVyIjogewogICAgICAiZW1haWwiOiAiam11b3phbkBnbWFpbC5jb20iLAogICAgICAibmFtZSI6ICJKb3JnZSBNdVx1MDBGMW96IFphblx1MDBGM24iLAogICAgICAiY291bnRyeSI6ICJTcGFpbiIsCiAgICAgICJ1cmwiOiAiaHR0cHM6Ly9qbXVvemFuLmdpdGh1Yi5pby9qb3JnZW11bnlvenouZ2l0aHViLmlvLyIKICAgIH0sCiAgICAiZGVzY3JpcHRpb24iOiAiR2VuZXJhdGUgQnJlcHMgaW4gR3Jhc3Nob3BwZXIgd2l0aCBwcm9tcHRzIChVc2luZyBaT09cdTAwMjdzIFRleHQgdG8gQ0FEIG1vZGVsKSBvciBmcm9tIGltYWdlcyAoVXNpbmcgT3BlbkFJXHUwMDI3cyB2aXNpb24gbW9kZWwpIiwKICAgICJjb3B5cmlnaHQiOiAiQ29weXJpZ2h0IFx1MDBBOSAyMDI0IEpvcmdlIE11XHUwMEYxb3ogWmFuXHUwMEYzbiIsCiAgICAibGljZW5zZSI6ICJNSVQiLAogICAgInVybCI6ICJodHRwczovL2ptdW96YW4uZ2l0aHViLmlvL2pvcmdlbXVueW96ei5naXRodWIuaW8vIgogIH0sCiAgInNldHRpbmdzIjogewogICAgImJ1aWxkUGF0aCI6ICJmaWxlOi8vL1VzZXJzL2pvcmdlbXV5by9EZXNrdG9wL0dIX0FJXzNEL2J1aWxkL3JoOCIsCiAgICAiYnVpbGRUYXJnZXQiOiB7CiAgICAgICJob3N0IjogewogICAgICAgICJuYW1lIjogIlJoaW5vM0QiLAogICAgICAgICJ2ZXJzaW9uIjogIjgiCiAgICAgIH0sCiAgICAgICJ0aXRsZSI6ICJSaGlubzNEICg4LiopIiwKICAgICAgInNsdWciOiAicmg4IgogICAgfSwKICAgICJwdWJsaXNoVGFyZ2V0IjogewogICAgICAidGl0bGUiOiAiTWNOZWVsIFlhayBTZXJ2ZXIiCiAgICB9CiAgfSwKICAiY29kZXMiOiBbXQp9";
    static readonly dynamic s_projectServer = default;
    static readonly object s_project = default;

    static ProjectComponentPlugin()
    {
      s_projectServer = ProjectInterop.GetProjectServer();
      if (s_projectServer is null)
      {
        RhinoApp.WriteLine($"Error loading Grasshopper plugin. Missing Rhino3D platform");
        return;
      }

      // get project
      dynamic dctx = ProjectInterop.CreateInvokeContext();
      dctx.Inputs["projectAssembly"] = typeof(ProjectComponentPlugin).Assembly;
      dctx.Inputs["projectId"] = s_projectId;
      dctx.Inputs["projectData"] = s_projectData;

      object project = default;
      if (s_projectServer.TryInvoke("plugins/v1/deserialize", dctx)
            && dctx.Outputs.TryGet("project", out project))
      {
        // server reports errors
        s_project = project;
      }
    }

    public override GH_LoadingInstruction PriorityLoad()
    {
      if (AssemblyInfo.PluginCategoryIcon is SD.Bitmap icon)
      {
        Grasshopper.Instances.ComponentServer.AddCategoryIcon("AI_Tools", icon);
      }
      Grasshopper.Instances.ComponentServer.AddCategorySymbolName("AI_Tools", "AI_Tools"[0]);

      return GH_LoadingInstruction.Proceed;
    }

    public static bool TryCreateScript(GH_Component ghcomponent, string serialized, out object script)
    {
      script = default;

      if (s_projectServer is null) return false;

      dynamic dctx = ProjectInterop.CreateInvokeContext();
      dctx.Inputs["component"] = ghcomponent;
      dctx.Inputs["project"] = s_project;
      dctx.Inputs["scriptData"] = serialized;

      if (s_projectServer.TryInvoke("plugins/v1/gh/deserialize", dctx))
      {
        return dctx.Outputs.TryGet("script", out script);
      }

      return false;
    }

    public static void DisposeScript(GH_Component ghcomponent, object script)
    {
      if (script is null)
        return;

      dynamic dctx = ProjectInterop.CreateInvokeContext();
      dctx.Inputs["component"] = ghcomponent;
      dctx.Inputs["project"] = s_project;
      dctx.Inputs["script"] = script;

      if (!s_projectServer.TryInvoke("plugins/v1/gh/dispose", dctx))
        throw new Exception("Error disposing Grasshopper script component");
    }
  }
}
