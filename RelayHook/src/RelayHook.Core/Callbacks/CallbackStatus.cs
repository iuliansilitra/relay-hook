namespace RelayHook.Core.Callbacks;

internal enum CallbackStatus : byte
{
    Pending = 0,
    Processing = 1,
    Completed = 2,
    Failed = 3
}
