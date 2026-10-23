namespace Mavka.Mobile;

public partial class MainActivity
{
    internal static bool HasActiveExchange { get; private set; }
    private ExchangeOperation? _exchangeOperation;
    private bool _resetRequested;
    private Android.Widget.TextView? _homeSyncStatus;
    private string? _lastExchangeStatus;
    private bool _exchangeHasResult;
    private void ReportExchangeStatus(Android.Widget.TextView? origin,string text,bool finished=false)
    {
        _lastExchangeStatus=text;_exchangeHasResult=finished;
        if(IsDestroyed)return;
        if(origin is not null)origin.Text=text;
        if(_screen=="home" && _homeSyncStatus is not null)_homeSyncStatus.Text=text;
    }

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
        public ExchangeOperation(MainActivity owner, CancellationToken parent,TimeSpan? timeout=null)
        {
            _owner = owner;
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(parent);
            _cancellation.CancelAfter(timeout??TimeSpan.FromMinutes(5));
        }
        public void Cancel() => _cancellation.Cancel();
        public void Dispose()
        {
            if (ReferenceEquals(_owner._exchangeOperation, this))
            {
                _owner._exchangeOperation = null; HasActiveExchange=false;
                if(!_owner._exchangeHasResult)_owner.ReportExchangeStatus(null,_cancellation.IsCancellationRequested?"Обмін скасовано. Збережені дані залишено.":"Обмін завершено.",true);
            }
            _cancellation.Dispose();
            _finished.TrySetResult();
        }
    }
    private ExchangeOperation BeginExchange(CancellationToken parent = default,TimeSpan? timeout=null)
    {
        if (_resetRequested || _exchangeOperation is not null || WidgetSyncJob.IsRunning)
            throw new InvalidOperationException("Попередній обмін ще завершується.");
        ReportExchangeStatus(null,"Обмін виконується. Його можна скасувати.");
        HasActiveExchange=true;
        return _exchangeOperation = new ExchangeOperation(this, parent,timeout);
    }
    private void CancelExchange(Android.Widget.TextView status)
    {
        if(WidgetSyncJob.IsRunning){_=WidgetSyncJob.CancelAndWait(this);ReportExchangeStatus(status,"Скасування обміну віджета…");return;}
        if (_exchangeOperation is null) { status.Text = "Активного обміну немає."; return; }
        _exchangeOperation.Cancel();
        ReportExchangeStatus(status,"Скасування обміну… Збережені документи залишаться в обліку.");
    }
#if MAVKA_BOOTSTRAP_QA
    private async Task HoldSyntheticExchangeForResetQa(bool progress=false)
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
            try{ShowHome();if(progress)ReportExchangeStatus(null,"Отримано 24 із 34 частин. Дані ще перевіряються.");await Task.Delay(Timeout.InfiniteTimeSpan,operation.Token);}
            finally{RelaySync.Gate.Release();}
        }
        catch(OperationCanceledException)when(operation.Token.IsCancellationRequested){}
        finally{_busy=false;}
    }
#endif
}
