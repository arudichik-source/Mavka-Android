namespace Mavka.Mobile;

public partial class MainActivity
{
    private ExchangeOperation? _exchangeOperation;
    private bool _resetRequested;

    // One owner for manual refresh, foreground polling and local recovery.
    // Cancellation is cooperative: reset waits for the transaction to roll back
    // and for its owner to release the gate before deleting any content.
    private sealed class ExchangeOperation : IDisposable
    {
        private readonly MainActivity _owner;
        private readonly CancellationTokenSource _cancellation;
        private readonly TaskCompletionSource _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token => _cancellation.Token;
        public Task Finished => _finished.Task;
        public ExchangeOperation(MainActivity owner, CancellationToken parent)
        {
            _owner = owner;
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(parent);
            _cancellation.CancelAfter(TimeSpan.FromMinutes(5));
        }
        public void Cancel() => _cancellation.Cancel();
        public void Dispose()
        {
            if (ReferenceEquals(_owner._exchangeOperation, this)) _owner._exchangeOperation = null;
            _cancellation.Dispose();
            _finished.TrySetResult();
        }
    }
    private ExchangeOperation BeginExchange(CancellationToken parent = default)
    {
        if (_resetRequested || _exchangeOperation is not null)
            throw new InvalidOperationException("Попередній обмін ще завершується.");
        return _exchangeOperation = new ExchangeOperation(this, parent);
    }
    private void CancelExchange(Android.Widget.TextView status)
    {
        if (_exchangeOperation is null) { status.Text = "Активного обміну немає."; return; }
        _exchangeOperation.Cancel();
        status.Text = "Скасування обміну… Збережені документи залишаться в обліку.";
    }
#if MAVKA_BOOTSTRAP_QA
    private async Task HoldSyntheticExchangeForResetQa()
    {
        // Isolated emulator only: hold the same operation/gate as a slow
        // transport so the UI test can request reset while exchange is busy.
        if(new UserProfileStore(this).Load()?.FullName!="SYNTHETIC-QA-USER" ||
            new GmailCredentialsStore(this).HasCredentials || new RelayIdentity(this).Enabled || new ExchangeKeyStore(this).HasKeyPair)
            throw new InvalidOperationException("Offline synthetic reset QA only.");
        using var operation=BeginExchange();_busy=true;
        try
        {
            await RelaySync.Gate.WaitAsync(operation.Token);
            try{ShowHome();await Task.Delay(Timeout.InfiniteTimeSpan,operation.Token);}
            finally{RelaySync.Gate.Release();}
        }
        catch(OperationCanceledException)when(operation.Token.IsCancellationRequested){}
        finally{_busy=false;}
    }
#endif
}
