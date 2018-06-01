using Android.Support.Design.Widget;
using System.Collections.Generic;
using Android.Views;
using System.Collections.Concurrent;
using System;
using Xamarin.Forms;
using Xamarin.Forms.Platform.Android;
using Android.Content;
using Google.AR.Core;
using Android.Opengl;
using XamarinFormsAR.Droid;
using Android.Widget;
using Javax.Microedition.Khronos.Opengles;
using Android.Util;
using XamarinFormsAR.Core;
using Plugin.CurrentActivity;

[assembly: ExportRenderer(typeof(ARView), typeof(ARViewRenderer))]
namespace XamarinFormsAR.Droid
{
    public class ARViewRenderer : ViewRenderer<ARView, GLSurfaceView>, Android.Views.View.IOnTouchListener, GLSurfaceView.IRenderer//PageRenderer, GLSurfaceView.IRenderer, Android.Views.View.IOnTouchListener
    {
        const string TAG = "HELLO-AR";

        GLSurfaceView mSurfaceView;
        Google.AR.Core.Config mDefaultConfig;
        Session mSession;
        BackgroundRenderer mBackgroundRenderer = new BackgroundRenderer();
        GestureDetector mGestureDetector;
        Snackbar mLoadingMessageSnackbar = null;
        Context _context;
        DisplayRotationHelper mDisplayRotationHelper;
        ObjectRenderer mVirtualObject = new ObjectRenderer();
        ObjectRenderer mVirtualObjectShadow = new ObjectRenderer();
        PlaneRenderer mPlaneRenderer = new PlaneRenderer();
        PointCloudRenderer mPointCloud = new PointCloudRenderer();
        static float[] mAnchorMatrix = new float[16];

        ConcurrentQueue<MotionEvent> mQueuedSingleTaps = new ConcurrentQueue<MotionEvent>();

        List<PlaneAttachment> mTouches = new List<PlaneAttachment>();

        public ARViewRenderer(Context context) : base(context)
        {
            _context = context;
        }

        protected override void OnElementChanged(ElementChangedEventArgs<ARView> e)
        {
            base.OnElementChanged(e);

            if (e.OldElement != null || Element == null)
            {
                return;
            }

            try
            {
                mSurfaceView = new GLSurfaceView(_context);
                mDisplayRotationHelper = new DisplayRotationHelper(_context);

                Java.Lang.Exception exception = null;
                string message = null;

                try
                {
                    mSession = new Session(_context);
                }
                //catch (UnavailableArcoreNotInstalledException ex)
                //{
                //    message = "Please install ARCore";
                //    exception = ex;
                //}
                //catch (UnavailableApkTooOldException ex)
                //{
                //    message = "Please update ARCore";
                //    exception = ex;
                //}
                //catch (UnavailableSdkTooOldException ex)
                //{
                //    message = "Please update this app";
                //    exception = ex;
                //}
                catch (Java.Lang.Exception ex)
                {
                    exception = ex;
                    message = "This device does not support AR";
                }

                if (message != null)
                {
                    Toast.MakeText(_context, message, ToastLength.Long).Show();
                    return;
                }

                var config = new Google.AR.Core.Config(mSession);
                if (!mSession.IsSupported(config))
                {
                    Toast.MakeText(_context, "This device does not support AR", ToastLength.Long).Show();
                    return;
                }

                mGestureDetector = new Android.Views.GestureDetector(_context, new SimpleTapGestureDetector
                {
                    SingleTapUpHandler = (MotionEvent arg) =>
                    {
                        onSingleTap(arg);
                        return true;
                    },
                    DownHandler = (MotionEvent arg) => true
                });

                mSurfaceView.SetOnTouchListener(this);

                mSurfaceView.PreserveEGLContextOnPause = true;
                mSurfaceView.SetEGLContextClientVersion(2);
                mSurfaceView.SetEGLConfigChooser(8, 8, 8, 8, 16, 0); // Alpha used for plane blending.
                mSurfaceView.SetRenderer(this);
                mSurfaceView.RenderMode = Rendermode.Continuously;

                SetNativeControl(mSurfaceView);

                mSession.Resume();
                mSurfaceView.OnResume();
                mDisplayRotationHelper.OnResume();

                showLoadingMessage();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(@"           ERROR: ", ex.Message);
            }
        }

        private void onSingleTap(MotionEvent e)
        {
            if (mQueuedSingleTaps.Count < 16)
                mQueuedSingleTaps.Enqueue(e);
        }

        public void OnSurfaceCreated(IGL10 gl, Javax.Microedition.Khronos.Egl.EGLConfig config)
        {
            GLES20.GlClearColor(0.1f, 0.1f, 0.1f, 1.0f);

            mBackgroundRenderer.CreateOnGlThread(_context);
            mSession.SetCameraTextureName(mBackgroundRenderer.TextureId);

            try
            {
                mVirtualObject.CreateOnGlThread(/*context=*/Context, "andy.obj", "andy.png");
                mVirtualObject.setMaterialProperties(0.0f, 3.5f, 1.0f, 6.0f);

                mVirtualObjectShadow.CreateOnGlThread(/*context=*/Context,
                    "andy_shadow.obj", "andy_shadow.png");
                mVirtualObjectShadow.SetBlendMode(ObjectRenderer.BlendMode.Shadow);
                mVirtualObjectShadow.setMaterialProperties(1.0f, 0.0f, 0.0f, 1.0f);
            }
            catch (Java.IO.IOException ex)
            {
                Log.Error(TAG, "Failed to read obj file");
            }

            try
            {
                mPlaneRenderer.CreateOnGlThread(/*context=*/_context, "trigrid.png");
            }
            catch (Java.IO.IOException ex)
            {
                Log.Error(TAG, "Failed to read plane texture");
            }
            mPointCloud.CreateOnGlThread(/*context=*/Context);
        }

        public void OnSurfaceChanged(IGL10 gl, int width, int height)
        {
            mDisplayRotationHelper.OnSurfaceChanged(width, height);
            GLES20.GlViewport(0, 0, width, height);
        }

        public void OnDrawFrame(IGL10 gl)
        {
            GLES20.GlClear(GLES20.GlColorBufferBit | GLES20.GlDepthBufferBit);

            try
            {
                if (mSession == null)
                    return;

                mDisplayRotationHelper.UpdateSessionIfNeeded(mSession);

                Google.AR.Core.Frame frame = mSession.Update();
                Camera camera = frame.Camera;

                MotionEvent tap = null;
                mQueuedSingleTaps.TryDequeue(out tap);

                if (tap != null && camera.TrackingState == TrackingState.Tracking)// && frame.GetTrackingState() == Google.AR.Core.Frame.TrackingState.Tracking)
                {
                    foreach (var hit in frame.HitTest(tap))
                    {
                        var trackable = hit.Trackable;

                        if (trackable is Plane && ((Plane)trackable).IsPoseInPolygon(hit.HitPose))                       
                        {
                            if (mTouches.Count >= 16)
                            {
                                //mSession.RemoveAnchors(new[] { mTouches[0].GetAnchor() });
                                mTouches.RemoveAt(0);
                            }
                            mTouches.Add(new PlaneAttachment((Plane)trackable, mSession.CreateAnchor(hit.HitPose)));

                            break;
                        }
                    }
                }

                mBackgroundRenderer.Draw(frame);

                if (camera.TrackingState == TrackingState.Paused)
                    return;

                float[] projmtx = new float[16];
                camera.GetProjectionMatrix(projmtx, 0, 0.1f, 100.0f);

                float[] viewmtx = new float[16];
                camera.GetViewMatrix(viewmtx, 0);

                var lightIntensity = frame.LightEstimate.PixelIntensity;

                var pointCloud = frame.AcquirePointCloud();
                mPointCloud.Update(pointCloud);
                mPointCloud.Draw(camera.DisplayOrientedPose, viewmtx, projmtx);

                pointCloud.Release();

                var planes = new List<Plane>();
                foreach (var p in mSession.GetAllTrackables(Java.Lang.Class.FromType(typeof(Plane))))
                {
                    var plane = (Plane)p;
                    planes.Add(plane);
                }

                if (mLoadingMessageSnackbar != null)
                {
                    foreach (var plane in planes)
                    {
                        if (plane.GetType() == Plane.Type.HorizontalUpwardFacing
                                && plane.TrackingState == TrackingState.Tracking)
                        {
                            hideLoadingMessage();
                            break;
                        }
                    }
                }

                mPlaneRenderer.DrawPlanes(planes, camera.DisplayOrientedPose, projmtx);

                float scaleFactor = 1.0f;
                foreach (var planeAttachment in mTouches)
                {
                    if (!planeAttachment.IsTracking)
                        continue;

                    planeAttachment.GetPose().ToMatrix(mAnchorMatrix, 0);

                    mVirtualObject.updateModelMatrix(mAnchorMatrix, scaleFactor);
                    mVirtualObjectShadow.updateModelMatrix(mAnchorMatrix, scaleFactor);
                    mVirtualObject.Draw(viewmtx, projmtx, lightIntensity);
                    mVirtualObjectShadow.Draw(viewmtx, projmtx, lightIntensity);
                }

            }
            catch (System.Exception ex)
            {
                Log.Error(TAG, "Exception on the OpenGL thread", ex);
            }
        }

        public bool OnTouch(Android.Views.View v, MotionEvent e)
        {
            return mGestureDetector.OnTouchEvent(e);
        }

        private void showLoadingMessage()
        {
            Device.BeginInvokeOnMainThread(async () =>
            {
                Android.App.Activity activity = CrossCurrentActivity.Current.Activity;
                Android.Views.View activityRootView = activity.FindViewById(Android.Resource.Id.Content);

                mLoadingMessageSnackbar = Snackbar.Make(activityRootView,
                "Searching for surfaces...", Snackbar.LengthIndefinite);

                mLoadingMessageSnackbar.View.SetBackgroundColor(Android.Graphics.Color.DarkGray);
                mLoadingMessageSnackbar.Show();
            });
        }

        private void hideLoadingMessage()
        {
            Device.BeginInvokeOnMainThread(async () =>
            {
                mLoadingMessageSnackbar.Dismiss();
                mLoadingMessageSnackbar = null;
            });

        }
    }

    class SimpleTapGestureDetector : GestureDetector.SimpleOnGestureListener
    {
        public Func<MotionEvent, bool> SingleTapUpHandler { get; set; }

        public override bool OnSingleTapUp(MotionEvent e)
        {
            return SingleTapUpHandler?.Invoke(e) ?? false;
        }

        public Func<MotionEvent, bool> DownHandler { get; set; }

        public override bool OnDown(MotionEvent e)
        {
            return DownHandler?.Invoke(e) ?? false;
        }
    }
}