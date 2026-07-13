using TypeGen.Core.SpecGeneration;

namespace Shared.Contracts;

public class Generator : GenerationSpec
{
  public Generator()
  {
    AddInterface<Class1>();
    AddInterface<OlehDto>();
  }
}