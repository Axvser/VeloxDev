using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VeloxDev.AI.Workflow.Protocols;

public abstract class ProtocolBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

public sealed class MoveNodeProtocol : ProtocolBase
{
    private int _nodeIndex;
    private double _offsetX;
    private double _offsetY;

    public int NodeIndex { get => _nodeIndex; set => SetField(ref _nodeIndex, value); }
    public double OffsetX { get => _offsetX; set => SetField(ref _offsetX, value); }
    public double OffsetY { get => _offsetY; set => SetField(ref _offsetY, value); }
}

public sealed class SetNodePositionProtocol : ProtocolBase
{
    private int _nodeIndex;
    private double _left;
    private double _top;
    private int _layer;

    public int NodeIndex { get => _nodeIndex; set => SetField(ref _nodeIndex, value); }
    public double Left { get => _left; set => SetField(ref _left, value); }
    public double Top { get => _top; set => SetField(ref _top, value); }
    public int Layer { get => _layer; set => SetField(ref _layer, value); }
}

public sealed class ResizeNodeProtocol : ProtocolBase
{
    private int _nodeIndex;
    private double _width;
    private double _height;

    public int NodeIndex { get => _nodeIndex; set => SetField(ref _nodeIndex, value); }
    public double Width { get => _width; set => SetField(ref _width, value); }
    public double Height { get => _height; set => SetField(ref _height, value); }
}

public sealed class DeleteNodeProtocol : ProtocolBase
{
    private int _nodeIndex;

    public int NodeIndex { get => _nodeIndex; set => SetField(ref _nodeIndex, value); }
}

public sealed class ConnectSlotsProtocol : ProtocolBase
{
    private int _senderNodeIndex;
    private int _senderSlotIndex;
    private int _receiverNodeIndex;
    private int _receiverSlotIndex;

    public int SenderNodeIndex { get => _senderNodeIndex; set => SetField(ref _senderNodeIndex, value); }
    public int SenderSlotIndex { get => _senderSlotIndex; set => SetField(ref _senderSlotIndex, value); }
    public int ReceiverNodeIndex { get => _receiverNodeIndex; set => SetField(ref _receiverNodeIndex, value); }
    public int ReceiverSlotIndex { get => _receiverSlotIndex; set => SetField(ref _receiverSlotIndex, value); }
}

public sealed class DisconnectSlotsProtocol : ProtocolBase
{
    private int _senderNodeIndex;
    private int _senderSlotIndex;
    private int _receiverNodeIndex;
    private int _receiverSlotIndex;

    public int SenderNodeIndex { get => _senderNodeIndex; set => SetField(ref _senderNodeIndex, value); }
    public int SenderSlotIndex { get => _senderSlotIndex; set => SetField(ref _senderSlotIndex, value); }
    public int ReceiverNodeIndex { get => _receiverNodeIndex; set => SetField(ref _receiverNodeIndex, value); }
    public int ReceiverSlotIndex { get => _receiverSlotIndex; set => SetField(ref _receiverSlotIndex, value); }
}

public sealed class ExecuteWorkProtocol : ProtocolBase
{
    private int _nodeIndex;
    private string? _parameter;

    public int NodeIndex { get => _nodeIndex; set => SetField(ref _nodeIndex, value); }
    public string? Parameter { get => _parameter; set => SetField(ref _parameter, value); }
}

public sealed class PatchPropertiesProtocol : ProtocolBase
{
    private int _nodeIndex;
    private string _jsonPatch = string.Empty;

    public int NodeIndex { get => _nodeIndex; set => SetField(ref _nodeIndex, value); }
    public string JsonPatch { get => _jsonPatch; set => SetField(ref _jsonPatch, value); }
}

public sealed class GetTypeSchemaProtocol : ProtocolBase
{
    private string _fullTypeName = string.Empty;

    public string FullTypeName { get => _fullTypeName; set => SetField(ref _fullTypeName, value); }
}

public sealed class ExecuteCommandProtocol : ProtocolBase
{
    private int _nodeIndex;
    private string _commandName = string.Empty;
    private string? _jsonParameter;

    public int NodeIndex { get => _nodeIndex; set => SetField(ref _nodeIndex, value); }
    public string CommandName { get => _commandName; set => SetField(ref _commandName, value); }
    public string? JsonParameter { get => _jsonParameter; set => SetField(ref _jsonParameter, value); }
}

public sealed class ExecuteCommandByIdProtocol : ProtocolBase
{
    private string _runtimeId = string.Empty;
    private string _commandName = string.Empty;
    private string? _jsonParameter;

    public string RuntimeId { get => _runtimeId; set => SetField(ref _runtimeId, value); }
    public string CommandName { get => _commandName; set => SetField(ref _commandName, value); }
    public string? JsonParameter { get => _jsonParameter; set => SetField(ref _jsonParameter, value); }
}

public sealed class CreateNodeProtocol : ProtocolBase
{
    private string _fullTypeName = string.Empty;
    private double _left;
    private double _top;

    public string FullTypeName { get => _fullTypeName; set => SetField(ref _fullTypeName, value); }
    public double Left { get => _left; set => SetField(ref _left, value); }
    public double Top { get => _top; set => SetField(ref _top, value); }
}

public sealed class CreateSlotProtocol : ProtocolBase
{
    private int _nodeIndex;
    private string _fullSlotTypeName = string.Empty;
    private string _channel = "OneBoth";

    public int NodeIndex { get => _nodeIndex; set => SetField(ref _nodeIndex, value); }
    public string FullSlotTypeName { get => _fullSlotTypeName; set => SetField(ref _fullSlotTypeName, value); }
    public string Channel { get => _channel; set => SetField(ref _channel, value); }
}
