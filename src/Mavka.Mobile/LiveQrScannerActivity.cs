using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Hardware.Camera2;
using Android.Hardware.Camera2.Params;
using Android.OS;
using Android.Views;
using Android.Widget;

namespace Mavka.Mobile;

// Private, in-app Camera2 preview. Frames stay in RAM; no photo app, JPEG, gallery
// write or network access. The existing registration confirmation validates QR data.
[Activity(Name="ua.mavka.mobile.LiveQrScannerActivity",Exported=false,
    ScreenOrientation=ScreenOrientation.Portrait,Theme="@android:style/Theme.Material.NoActionBar")]
public sealed class LiveQrScannerActivity:Activity,TextureView.ISurfaceTextureListener
{
    public const string ResultKey="mavka-scanned-qr";
    private const int PermissionRequest=2301;
    private TextureView _preview=null!;
    private TextView _status=null!;
    private CameraDevice? _camera;
    private CameraCaptureSession? _session;
    private Surface? _surface;
    private Handler? _main;
    private CancellationTokenSource? _poll;
    private bool _resumed,_opening,_decoding,_done;
    private int _generation;
    private DeviceState? _deviceState;
    private SessionState? _sessionState;

    protected override void OnCreate(Bundle? state)
    {
        base.OnCreate(state);
        _main=new Handler(Looper.MainLooper!);
        var root=new LinearLayout(this){Orientation=Orientation.Vertical};root.SetPadding(18,36,18,24);
        root.SetOnApplyWindowInsetsListener(new ScannerInsets((int)(16*Resources!.DisplayMetrics!.Density)));
        root.AddView(new TextView(this){Text="Сканер QR Мавки",TextSize=24});
        _status=new TextView(this){Text="Наведіть телефон на QR реєстрації на екрані ПК. Знімок робити не потрібно.",TextSize=16};root.AddView(_status);
        _preview=new TextureView(this){SurfaceTextureListener=this};root.AddView(_preview,new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent,0,1));
        var back=new Button(this){Text="Скасувати сканування"};back.SetAllCaps(false);back.Click+=(_,_)=>Finish();root.AddView(back);
        SetContentView(root);
    }
    protected override void OnResume()
    {
        base.OnResume();_resumed=true;
        if(CheckSelfPermission(Manifest.Permission.Camera)!=Permission.Granted)RequestPermissions([Manifest.Permission.Camera],PermissionRequest);
        else OpenCamera();
    }
    public override void OnRequestPermissionsResult(int requestCode,string[] permissions,Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode,permissions,grantResults);
        if(requestCode!=PermissionRequest)return;
        if(grantResults.Length>0 && grantResults[0]==Permission.Granted)OpenCamera();
        else _status.Text="Дозвольте камеру в налаштуваннях Android або поверніться й виберіть фото QR-коду.";
    }
    private void OpenCamera()
    {
        if(!_resumed || _done || _opening || _camera is not null || !_preview.IsAvailable || CheckSelfPermission(Manifest.Permission.Camera)!=Permission.Granted)return;
        try
        {
            var manager=(CameraManager)GetSystemService(CameraService)!;
            var ids=manager.GetCameraIdList();
            var id=ids.FirstOrDefault(i=>manager.GetCameraCharacteristics(i).Get(CameraCharacteristics.LensFacing)?.ToString()=="1")??ids.FirstOrDefault();
            if(id is null)throw new InvalidOperationException("Камера недоступна.");
            using var characteristics=manager.GetCameraCharacteristics(id);
            var map=(StreamConfigurationMap)characteristics.Get(CameraCharacteristics.ScalerStreamConfigurationMap)!;
            var sizes=map.GetOutputSizes(Java.Lang.Class.FromType(typeof(SurfaceTexture)))!;
            var size=sizes.Where(s=>s.Width<=1920 && s.Height<=1080).OrderBy(s=>Math.Abs(s.Width-1280)+Math.Abs(s.Height-720)).FirstOrDefault()??sizes.OrderBy(s=>(long)s.Width*s.Height).First();
            _preview.SurfaceTexture!.SetDefaultBufferSize(size.Width,size.Height);
            // Fit the preview without stretching the QR's square modules.
            var sensor=int.Parse(characteristics.Get(CameraCharacteristics.SensorOrientation)!.ToString()!);
            var ratio=sensor is 90 or 270?(double)size.Height/size.Width:(double)size.Width/size.Height;
            var w=_preview.Width;var h=_preview.Height;
            using var transform=new Matrix();
            if(w/(double)h>ratio)transform.SetScale((float)(h*ratio/w),1,w/2f,h/2f);
            else transform.SetScale(1,(float)(w/ratio/h),w/2f,h/2f);
            _preview.SetTransform(transform);
            _opening=true;_deviceState=new DeviceState(this,_generation);manager.OpenCamera(id,_deviceState,_main);
        }
        catch(Exception error){Android.Util.Log.Warn("MaVkaQrScanner",error.GetType().FullName);_opening=false;_status.Text="Не вдалося відкрити камеру. Закрийте іншу програму камери або виберіть фото QR у профілі.";}
    }
    private void BeginPreview(CameraDevice camera)
    {
        _opening=false;if(!_resumed || _done){camera.Close();return;}_camera=camera;
        try{_surface=new Surface(_preview.SurfaceTexture!);_sessionState=new SessionState(this,_generation);camera.CreateCaptureSession(new List<Surface>{_surface},_sessionState,_main);}
        catch(Exception){_status.Text="Не вдалося запустити сканер. Поверніться й виберіть фото QR.";CloseCamera();}
    }
    private void PreviewReady(CameraCaptureSession session)
    {
        if(!_resumed || _camera is null || _surface is null){session.Close();return;}
        try
        {
            _session=session;using var request=_camera.CreateCaptureRequest(CameraTemplate.Preview);request.AddTarget(_surface);
            using var capture=request.Build();session.SetRepeatingRequest(capture,null,_main);
            _status.Text="Сканування QR: наведіть камеру на код із ПК.";
            _poll?.Cancel();_poll=new();var token=_poll.Token;
            _=Task.Run(async()=>{try{while(!token.IsCancellationRequested){await Task.Delay(350,token);RunOnUiThread(DecodeFrame);}}catch(System.OperationCanceledException){}},token);
        }
        catch(Exception){_status.Text="Камера не почала сканування. Поверніться й виберіть фото QR.";CloseCamera();}
    }
    private async void DecodeFrame()
    {
        if(!_resumed || _done || _decoding || !_preview.IsAvailable || _camera is null)return;
        _decoding=true;
        try
        {
            var scale=Math.Min(1,1200.0/Math.Max(_preview.Width,_preview.Height));
            using var bitmap=_preview.GetBitmap(Math.Max(1,(int)(_preview.Width*scale)),Math.Max(1,(int)(_preview.Height*scale)));
            if(bitmap is null)return;
            var raw=await Task.Run(()=>QrPhotoDecoder.ReadQr(bitmap));
            if(!_resumed || _done)return;_done=true;
            SetResult(Result.Ok,new Intent().PutExtra(ResultKey,raw));Finish();
        }
        catch(InvalidOperationException){/* QR not yet visible: inspect the next frame. */}
        catch(Exception){if(_resumed)_status.Text="Наведіть камеру на весь QR на ПК. Тримайте телефон нерухомо.";}
        finally{_decoding=false;}
    }
    private void CloseCamera()
    {
        _generation++;
        _poll?.Cancel();_poll?.Dispose();_poll=null;
        try{_session?.Close();}catch{} _session=null;
        try{_camera?.Close();}catch{} _camera=null;
        _surface?.Dispose();_surface=null;_opening=false;
    }
    protected override void OnPause(){_resumed=false;CloseCamera();base.OnPause();}
    public void OnSurfaceTextureAvailable(SurfaceTexture surface,int width,int height)=>OpenCamera();
    public void OnSurfaceTextureSizeChanged(SurfaceTexture surface,int width,int height){}
    public bool OnSurfaceTextureDestroyed(SurfaceTexture surface){CloseCamera();return true;}
    public void OnSurfaceTextureUpdated(SurfaceTexture surface){}
    private sealed class ScannerInsets(int padding):Java.Lang.Object,View.IOnApplyWindowInsetsListener
    {
        public WindowInsets OnApplyWindowInsets(View? view,WindowInsets? insets)
        {
            if(view is not null && insets is not null)view.SetPadding(padding+insets.SystemWindowInsetLeft,padding+insets.SystemWindowInsetTop,padding+insets.SystemWindowInsetRight,padding+insets.SystemWindowInsetBottom);
            return insets!;
        }
    }
    private sealed class DeviceState(LiveQrScannerActivity owner,int generation):CameraDevice.StateCallback
    {
        public override void OnOpened(CameraDevice camera){if(owner._generation==generation)owner.BeginPreview(camera);else camera.Close();}
        public override void OnDisconnected(CameraDevice camera){camera.Close();if(owner._generation==generation)owner.CloseCamera();}
        public override void OnError(CameraDevice camera,CameraError error){camera.Close();if(owner._generation!=generation)return;owner.CloseCamera();owner._status.Text="Камера недоступна. Поверніться й виберіть фото QR.";}
    }
    private sealed class SessionState(LiveQrScannerActivity owner,int generation):CameraCaptureSession.StateCallback
    {
        public override void OnConfigured(CameraCaptureSession session){if(owner._generation==generation)owner.PreviewReady(session);else session.Close();}
        public override void OnConfigureFailed(CameraCaptureSession session){session.Close();if(owner._generation!=generation)return;owner.CloseCamera();owner._status.Text="Камера не підтримала попередній перегляд. Виберіть фото QR.";}
    }
}
