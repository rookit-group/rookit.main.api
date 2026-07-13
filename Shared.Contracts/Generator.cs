using System.Reflection;
using TypeGen.Core.SpecGeneration;

namespace Shared.Contracts;

public class Generator : GenerationSpec
{
  public Generator()
  {
    var types = Assembly.GetExecutingAssembly()
      .GetTypes()
      .Where(t =>
        t.IsPublic &&             // only top-level public types (exported contract DTOs)
        !t.IsNested               // skip nested types, e.g. compiler-generated lambda closures
      );

    foreach (var type in types)
    {
      if (type.IsEnum)
      {
        AddEnum(type);
      }
      else if (type.IsClass && type != typeof(Generator))
      {
        AddInterface(type);
      }
    }
  }
}