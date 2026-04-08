using AdocNet.Ast;
using AdocNet.Extensions;

namespace AdocNet.TestSignedExtension;

public class SignedBlockProcessor : IBlockProcessor
{
    public bool CanProcess(BlockNode node) => false;
    public bool Process(BlockNode node, RenderContext context) => false;
}
