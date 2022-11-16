namespace libCanFestivalConsole.Sdk;

public enum NMTState
{
    BOOT = 0,
    STOPPED = 4,
    OPERATIONAL = 5,
    PRE_OPERATIONAL = 0x7f,
    INVALID = 0xff
}
public class CanFestivalNMTState
{
    public NMTState CurrentState { get; set; }
    public NMTState LastState { get; set; }
    public DateTime LastHeartbeat { get; set; }

    public Action<NMTState> NMTBootHandler = null;

    public CanFestivalNMTState()
    {
        CurrentState = NMTState.INVALID;
        LastState = NMTState.INVALID;
    }

    public void ChangeState(NMTState newState)
    {
        LastState = CurrentState;
        CurrentState = newState;
        LastHeartbeat = DateTime.Now;

        if (newState == NMTState.BOOT)
        {
            if (CurrentState != LastState && NMTBootHandler != null)
            {
                NMTBootHandler?.Invoke(CurrentState);
            }
        }
    }
}