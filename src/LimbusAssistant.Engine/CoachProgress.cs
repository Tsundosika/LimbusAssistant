namespace Tsundosika.LimbusAssistant.Engine;

public sealed class CoachProgress
{
    readonly object _gate = new();
    IReadOnlyList<BestMoveAdvice> _moves = [];
    bool[] _done = [];
    int _turnNumber;
    CoachProgressState _state = CoachProgressState.Empty;

    public CoachProgressState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public void Reset(IReadOnlyList<BestMoveAdvice> moves)
    {
        lock (_gate)
        {
            _moves = moves;
            _done = new bool[moves.Count];
            RebuildState();
        }
    }

    public void NewTurn(IReadOnlyList<BestMoveAdvice> moves)
    {
        lock (_gate)
        {
            _turnNumber++;
            _moves = moves;
            _done = new bool[moves.Count];
            RebuildState();
        }
    }

    public void Rebase(IReadOnlyList<BestMoveAdvice> moves)
    {
        lock (_gate)
        {
            var doneKeys = new List<string>();
            for (var i = 0; i < _moves.Count && i < _done.Length; i++)
            {
                if (_done[i])
                {
                    doneKeys.Add(Key(_moves[i]));
                }
            }
            _moves = moves;
            _done = new bool[moves.Count];
            for (var i = 0; i < moves.Count; i++)
            {
                var key = Key(moves[i]);
                var match = doneKeys.IndexOf(key);
                if (match >= 0)
                {
                    _done[i] = true;
                    doneKeys.RemoveAt(match);
                }
            }
            RebuildState();
        }
    }

    static string Key(BestMoveAdvice move) => $"{move.IdentityName}|{move.SkillName}|{move.TargetSkillName}";

    public bool ObservePairing(string? identityName, string? skillName, string? enemySkillName)
    {
        if (identityName is null || skillName is null)
        {
            return false;
        }
        lock (_gate)
        {
            for (var i = 0; i < _moves.Count; i++)
            {
                if (_done[i])
                {
                    continue;
                }
                var move = _moves[i];
                if (move.IdentityName != identityName || move.SkillName != skillName)
                {
                    continue;
                }
                if (move.TargetSkillName is not null && move.TargetSkillName != enemySkillName)
                {
                    continue;
                }
                _done[i] = true;
                RebuildState();
                return true;
            }
        }
        return false;
    }

    public bool AdvanceManually()
    {
        lock (_gate)
        {
            for (var i = 0; i < _done.Length; i++)
            {
                if (!_done[i])
                {
                    _done[i] = true;
                    RebuildState();
                    return true;
                }
            }
        }
        return false;
    }

    void RebuildState()
    {
        var current = -1;
        for (var i = 0; i < _done.Length; i++)
        {
            if (!_done[i])
            {
                current = i;
                break;
            }
        }
        _state = new CoachProgressState(_done.ToArray(), current, _turnNumber);
    }
}
