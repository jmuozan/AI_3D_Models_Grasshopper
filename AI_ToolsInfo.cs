using System;
using System.Drawing;
using Grasshopper;
using Grasshopper.Kernel;

namespace AI_Tools
{
  public class AI_ToolsInfo : GH_AssemblyInfo
  {
    public override string Name => "AI_Tools Info";

    //Return a 24x24 pixel bitmap to represent this GHA library.
    public override Bitmap Icon => null;

    //Return a short string describing the purpose of this GHA library.
    public override string Description => "";

    public override Guid Id => new Guid("d2c5c87c-5971-4e3b-963e-055ed4b8678f");

    //Return a string identifying you or your company.
    public override string AuthorName => "";

    //Return a string representing your preferred contact details.
    public override string AuthorContact => "";

    //Return a string representing the version.  This returns the same version as the assembly.
    public override string AssemblyVersion => GetType().Assembly.GetName().Version.ToString();
  }
}