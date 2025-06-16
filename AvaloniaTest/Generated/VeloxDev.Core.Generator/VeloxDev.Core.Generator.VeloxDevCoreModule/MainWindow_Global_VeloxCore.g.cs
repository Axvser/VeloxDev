#nullable enable

namespace Global
{

   public partial class MainWindow
   {

      private global::System.Threading.CancellationTokenSource? cts_mono = null;

      private bool _canmonobehaviour = false;
      public bool CanMonoBehaviour
      {
          get => _canmonobehaviour;
          set
          {
              if(_canmonobehaviour != value)
              {
                  _canmonobehaviour = value;
                  if (value)
                  {
                      var monofunc = new global::System.Func<global::System.Threading.Tasks.Task>(async () =>
                      {
                          await _inner_Update();
                      });
                      monofunc?.Invoke();
                  }
                  else
                  {
                      _innerCleanMonoToken();
                  }
              }
          }
      }

      private async global::System.Threading.Tasks.Task _inner_Update()
      {
          _innerCleanMonoToken();

          var newmonocts = new global::System.Threading.CancellationTokenSource();
          cts_mono = newmonocts;

          try
          {
             if(CanMonoBehaviour) Start();

             while (CanMonoBehaviour && !newmonocts.Token.IsCancellationRequested)
             {
                 Update();
                 LateUpdate();
                 await global::System.Threading.Tasks.Task.Delay(60,newmonocts.Token);
             }
          }
          catch (global::System.Exception ex)
          {
              global::System.Diagnostics.Debug.WriteLine(ex.Message);
          }
          finally
          {
              if (global::System.Threading.Interlocked.CompareExchange(ref cts_mono, null, newmonocts) == newmonocts) 
              {
                  newmonocts.Dispose();
              }
              ExitMonoBehaviour();
          }
      }

      partial void Start();
      partial void Update();
      partial void LateUpdate();
      partial void ExitMonoBehaviour();

      private void _innerCleanMonoToken()
      {
          var oldCts = global::System.Threading.Interlocked.Exchange(ref cts_mono, null);
          if (oldCts != null)
          {
              try { oldCts.Cancel(); } catch { }
              oldCts.Dispose();
          }
      }


   }
}

