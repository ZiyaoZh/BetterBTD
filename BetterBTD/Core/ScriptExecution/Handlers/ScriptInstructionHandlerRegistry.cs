using BetterBTD.Models.ScriptEditor;

namespace BetterBTD.Core.ScriptExecution.Handlers;

public sealed class ScriptInstructionHandlerRegistry
{
    private static readonly Lazy<ScriptInstructionHandlerRegistry> InstanceHolder = new(() => new ScriptInstructionHandlerRegistry());

    private readonly Dictionary<ScriptCommandType, IScriptInstructionHandler> _handlers = [];

    private ScriptInstructionHandlerRegistry()
    {
        Register(new PlaceMonkeyInstructionHandler());
        Register(new UpgradeMonkeyInstructionHandler());
        Register(new SwitchMonkeyTargetInstructionHandler());
        Register(new SetMonkeyAbilityInstructionHandler());
        Register(new SellMonkeyInstructionHandler());
        Register(new PlaceHeroInventoryInstructionHandler());
        Register(new ActivateAbilityInstructionHandler());
        Register(new MouseClickInstructionHandler());
        Register(new NextRoundInstructionHandler());
        Register(new WaitInstructionHandler());
        Register(new ModifyMonkeyCoordinateInstructionHandler());
        Register(new FreeplayBoundaryInstructionHandler());
        Register(new CommentInstructionHandler());
    }

    public static ScriptInstructionHandlerRegistry Instance => InstanceHolder.Value;

    public void Register(IScriptInstructionHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handlers[handler.CommandType] = handler;
    }

    public IScriptInstructionHandler GetRequiredHandler(ScriptCommandType commandType)
    {
        if (_handlers.TryGetValue(commandType, out var handler))
        {
            return handler;
        }

        throw new InvalidOperationException($"No instruction handler was registered for '{commandType}'.");
    }
}
