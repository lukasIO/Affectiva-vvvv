#region usings
using System;
using System.IO;
using System.ComponentModel.Composition;

using VVVV.PluginInterfaces.V1;
using VVVV.PluginInterfaces.V2;

using VVVV.Utils.VColor;
using VVVV.Utils.VMath;
using Affdex;
using System.Drawing;
using System.Drawing.Imaging;
using VVVV.CV.Core;
using Emgu.CV;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Diagnostics;

using VVVV.Core.Logging;


using System.Collections.Generic;
using System.Threading;

#endregion usings

namespace VVVV.Nodes
{

    public class FrameEventArgs : EventArgs
    {
        public Affdex.Frame frame;
    }

    public delegate void InitializedHandler(object sender, EventArgs e);
    public delegate void FrameHandler(object sender, FrameEventArgs e);

    #region PluginInfo
    [PluginInfo(Name = "FaceRecognition", Category = "Value", Version = "Affectiva", Help = "Basic template with one value in/out", Tags = "")]
    #endregion PluginInfo
    public class AffectivaValueFaceRecognitionNode : IPluginEvaluate, IDisposable, IPartImportsSatisfiedNotification
    {
        #region fields & pins
        [Input("Input")]
        public ISpread<CVImageLink> FInput;

        [Input("DetectMode")]
        public IDiffSpread<Affdex.FaceDetectorMode> FDetectMode;

        [Input("MaximumFaces", DefaultValue = 2)]
        public IDiffSpread<uint> FMaxFaces;

        [Input("Initialize", IsBang = true)]
        public ISpread<bool> FInitalizeIn;

        [Output("Count")]
        public ISpread<double> FFaceCountOut;

        [Output("FeaturePoints")]
        public ISpread<Vector2D> FFeaturePoints;

        [Output("Joy")]
        public ISpread<float> FJoyOut;

        [Output("Anger")]
        public ISpread<float> FAngerOut;

        [Output("Sadness")]
        public ISpread<float> FSadnessOut;

        [Output("Contempt")]
        public ISpread<float> FContemptOut;

        [Output("Gender")]
        public ISpread<string> FGenderOut;

        [Output("Age")]
        public ISpread<string> FAgeOut;

        public event InitializedHandler Initialized;
        public event FrameHandler FrameRead;

        [Import()]
        public ILogger FLogger;


        #endregion fields & pins

        Stopwatch stopwatch;

        Affdex.Detector detector = null;
        private bool isInitalized = false;
        ProcessVideo videoForm;

        Task trackingTask;

        public void Dispose()
        {

            detector.stop();
            videoForm = null;
            detector.Dispose();
            detector = null;
            stopwatch.Stop();
            stopwatch = null;
        }

        public void OnImportsSatisfied()
        {
            stopwatch = new Stopwatch();
            stopwatch.Start();
            /*
            Initialized += OnInitialized;
            FrameRead += OnFrameRead;
            */
        }

        /*
        public void OnFrameRead(object sender, FrameEventArgs e)
        {
            if (trackingTask.IsCompleted)
            {
                trackingTask = new Task(() => ((Affdex.FrameDetector)detector).process(e.frame));
                trackingTask.Start();
            }

        }


        public void OnInitialized(object sender, EventArgs e)
        {
            isInitalized = true;
        }

        */

        public void InitializeDetector()
        {

            if (detector != null && detector.isRunning())
            {
                detector.stop();
                detector.Dispose();
            }

            detector = new Affdex.FrameDetector(10, 20, FMaxFaces[0], FDetectMode[0]);


            if (detector != null)
            {

                videoForm = new ProcessVideo(detector, FLogger);
                try
                {
                    string dataPath = "C:\\Program Files\\Affectiva\\AffdexSDK\\data";
                    detector.setClassifierPath(dataPath);
                }
                catch (Exception e)
                {

                    FLogger.Log(LogType.Debug, e.ToString());
                }

                detector.setDetectAllEmotions(true);
                detector.setDetectAllExpressions(true);
                detector.setDetectAllEmojis(true);
                detector.setDetectAllAppearances(true);
                detector.start();

                if (Initialized != null)
                {
                    Initialized(this, EventArgs.Empty);
                }


            }
        }

        //called when data for any output pin is requested
        public void Evaluate(int SpreadMax)
        {
            FFaceCountOut.SliceCount = SpreadMax;

            //CVImageLink inputimage = FInput[0];
            if (FInitalizeIn[0] || FDetectMode.IsChanged || FMaxFaces.IsChanged)
            {
                isInitalized = false;

                InitializeDetector();
                isInitalized = true;
                //new Task(InitializeDetector).Start();


                
            }


            if (isInitalized)
            {


                FInput[0].LockForReading();
                var bmp = LoadFrameFromMemory(FInput[0].FrontImage.GetImage().Bitmap);
                FInput[0].ReleaseForReading();

                var frameArgs = new FrameEventArgs();

                /*frameArgs.frame = bmp;
                if (FrameRead != null)
                    FrameRead(this, frameArgs);
                */
                ((Affdex.FrameDetector)detector).process(bmp);





            }



            FFaceCountOut[0] = videoForm.faces.Count;
            FJoyOut.SliceCount = FAngerOut.SliceCount = FContemptOut.SliceCount = FSadnessOut.SliceCount = FAgeOut.SliceCount = FGenderOut.SliceCount = videoForm.faces.Count;
            int count = 0;
            foreach (var face in videoForm.faces)
            {
                FFeaturePoints.SliceCount = face.Value.FeaturePoints.GetLength(0);
                int i = 0;
                foreach (var featurepoint in face.Value.FeaturePoints)
                {
                    Vector2D point = new Vector2D(featurepoint.X, featurepoint.Y);
                    FFeaturePoints[i] = point;
                    i++;
                }
                FJoyOut[count] = face.Value.Emotions.Joy;
                FAngerOut[count] = face.Value.Emotions.Anger;
                FSadnessOut[count] = face.Value.Emotions.Sadness;
                FContemptOut[count] = face.Value.Emotions.Contempt;

                FGenderOut[count] = face.Value.Appearance.Gender.ToString();
                FAgeOut[count] = face.Value.Appearance.Age.ToString();


                count++;

            }




        }

        public Affdex.Frame LoadFrameFromMemory(Bitmap bitmap)
        {
            // Lock the bitmap's bits.
            Rectangle rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            BitmapData bmpData = bitmap.LockBits(rect, ImageLockMode.ReadWrite, bitmap.PixelFormat);

            // Get the address of the first line.
            IntPtr ptr = bmpData.Scan0;

            // Declare an array to hold the bytes of the bitmap. 
            int numBytes = bitmap.Width * bitmap.Height * 3;
            byte[] rgbValues = new byte[numBytes];

            int data_x = 0;
            int ptr_x = 0;
            int row_bytes = bitmap.Width * 3;

            // The bitmap requires bitmap data to be byte aligned.
            // http://stackoverflow.com/questions/20743134/converting-opencv-image-to-gdi-bitmap-doesnt-work-depends-on-image-size

            for (int y = 0; y < bitmap.Height; y++)
            {
                Marshal.Copy(ptr + ptr_x, rgbValues, data_x, row_bytes);//(pixels, data_x, ptr + ptr_x, row_bytes);
                data_x += row_bytes;
                ptr_x += bmpData.Stride;
            }

            bitmap.UnlockBits(bmpData);

            return new Affdex.Frame(bitmap.Width, bitmap.Height, rgbValues, Affdex.Frame.COLOR_FORMAT.RGB, stopwatch.ElapsedMilliseconds);
        }



        public class ProcessVideo : Affdex.ProcessStatusListener, Affdex.ImageListener
        {
            ReaderWriterLockSlim rwLock;

            public ProcessVideo(Affdex.Detector detector, ILogger logger)
            {

                this.detector = detector;
                detector.setImageListener(this);
                detector.setProcessStatusListener(this);
                rwLock = new ReaderWriterLockSlim();
                this.FLogger = logger;

            }

            ILogger FLogger;

            public void onImageCapture(Affdex.Frame frame)
            {
                frame.Dispose();

            }

            public void onImageResults(Dictionary<int, Affdex.Face> faces, Affdex.Frame frame)
            {

                process_fps = 1.0f / (frame.getTimestamp() - process_last_timestamp);
                process_last_timestamp = frame.getTimestamp();
                //System.Console.WriteLine(" pfps: {0}", process_fps.ToString());

                byte[] pixels = frame.getBGRByteArray();
                this.img = new Bitmap(frame.getWidth(), frame.getHeight(), PixelFormat.Format24bppRgb);
                var bounds = new Rectangle(0, 0, frame.getWidth(), frame.getHeight());
                BitmapData bmpData = img.LockBits(bounds, ImageLockMode.WriteOnly, img.PixelFormat);
                IntPtr ptr = bmpData.Scan0;

                int data_x = 0;
                int ptr_x = 0;
                int row_bytes = frame.getWidth() * 3;

                // The bitmap requires bitmap data to be byte aligned.
                // http://stackoverflow.com/questions/20743134/converting-opencv-image-to-gdi-bitmap-doesnt-work-depends-on-image-size

                for (int y = 0; y < frame.getHeight(); y++)
                {
                    Marshal.Copy(pixels, data_x, ptr + ptr_x, row_bytes);
                    data_x += row_bytes;
                    ptr_x += bmpData.Stride;
                }
                img.UnlockBits(bmpData);
                rwLock.EnterReadLock();
                this.faces = faces;
                rwLock.ExitReadLock();
                frame.Dispose();
            }



            public void onProcessingException(Affdex.AffdexException A_0)
            {
                FLogger.Log(LogType.Debug, "Encountered an exception while processing {0}", A_0.ToString());
            }

            public void onProcessingFinished()
            {
                FLogger.Log(LogType.Debug, "Processing finished successfully");
            }

            Affdex.FeaturePoint minPoint(Affdex.FeaturePoint[] points)
            {
                Affdex.FeaturePoint ret = points[0];
                foreach (Affdex.FeaturePoint point in points)
                {
                    if (point.X < ret.X) ret.X = point.X;
                    if (point.Y < ret.Y) ret.Y = point.Y;
                }
                return ret;
            }

            Affdex.FeaturePoint maxPoint(Affdex.FeaturePoint[] points)
            {
                Affdex.FeaturePoint ret = points[0];
                foreach (Affdex.FeaturePoint point in points)
                {
                    if (point.X > ret.X) ret.X = point.X;
                    if (point.Y > ret.Y) ret.Y = point.Y;
                }
                return ret;
            }



            private float process_last_timestamp = -1.0f;
            private float process_fps = -1.0f;

            private Bitmap img { get; set; }
            public Dictionary<int, Affdex.Face> faces { get; private set; }
            private Affdex.Detector detector { get; set; }

        }
    }
}

