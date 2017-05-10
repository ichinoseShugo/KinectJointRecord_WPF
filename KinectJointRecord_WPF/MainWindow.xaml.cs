using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Microsoft.Kinect;
using System.ComponentModel;

namespace KinectJointRecord_WPF
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>
        /// Kinectセンサーを扱う変数
        /// </summary>
        private KinectSensor kinect = null;
        /// <summary>
        /// Kinectセンサーからの画像情報を受け取るバッファ
        /// </summary>
        private byte[] pixelBuffer = null;

        /// <summary>
        /// 画面に表示するビットマップ
        /// </summary>
        private WriteableBitmap bmpBuffer = null;
        /// <summary>
        /// ビットマップデータのリスト
        /// </summary>
        private List<WriteableBitmap> bmpList = new List<WriteableBitmap>();

        /// <summary>
        /// マイドキュメント下のKinectフォルダへのパス
        /// </summary>
        static string pathKinect = Environment.GetFolderPath(Environment.SpecialFolder.Personal) + "/Kinect";
        /// <summary>
        /// 保存先のフォルダ
        /// </summary>
        static string pathSaveFolder = null;
        /// <summary>
        /// 日付と時刻
        /// </summary>
        static public DateTime dt = DateTime.Now;
        /// <summary>
        /// 座標書き込み用ストリーム
        /// </summary>
        private StreamWriter sw;
        /// <summary>
        /// 画像や座標を記録するかのフラグ
        /// </summary>
        private bool RecFlag = false;
        /// <summary>
        /// 画像を保存するかのフラグ
        /// </summary>
        private bool SaveFlag = false;
        /// <summary>
        /// 時間計測ストップウォッチ
        /// </summary>
        System.Diagnostics.Stopwatch StopWatch = new System.Diagnostics.Stopwatch();
        /// <summary>
        /// 記録対象のジョイントの番号
        /// </summary>
        private JointType RecordJType = JointType.HandLeft;

        public MainWindow()
        {
            InitializeComponent();
            Closing += WindowClosing;
        }

        /// <summary>
        /// 初期化処理(Kinectセンサーやバッファ類の初期化)
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void WindowLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (KinectSensor.KinectSensors.Count == 0)
                    throw new Exception("Kinectが接続されていません");

                // Kinectセンサーの取得
                kinect = KinectSensor.KinectSensors[0];

                //スケルトン平滑化パラメータ
                TransformSmoothParameters parameters = new TransformSmoothParameters();
                parameters.Smoothing = 0.2f;
                parameters.Correction = 0.8f;
                parameters.Prediction = 0.0f;
                parameters.JitterRadius = 0.5f;
                parameters.MaxDeviationRadius = 0.5f;

                //color,skeletonの有効化(引数は解像度とフレームレート)
                kinect.ColorStream.Enable(ColorImageFormat.RgbResolution640x480Fps30);
                kinect.SkeletonStream.Enable(parameters);

                //バッファ(アプリケーションデータ)の初期化            
                //Kinectのカラーストリームの画像情報を一時的に読み込むバッファ。フレームごとに再利用するので領域だけ確保
                pixelBuffer = new byte[kinect.ColorStream.FramePixelDataLength];
                //MainWindowのImageコントロール(rgbImage)にセットするビットマップオブジェクトフレームごとに書き直すのでWriteableBitmapを使う
                bmpBuffer = new WriteableBitmap(kinect.ColorStream.FrameWidth,
                                                kinect.ColorStream.FrameHeight,
                                                96, 96, PixelFormats.Bgr32, null);
                rgbImage.Source = bmpBuffer;

                //ファイル書き込み用のMY Document 直下のKinectフォルダを作成
                Directory.CreateDirectory(pathKinect);

                //カラーフレームのイベントハンドラの登録
                kinect.ColorFrameReady += ColorImageReady;

                //スケルトンフレームのイベントハンドラの登録
                kinect.SkeletonFrameReady += SkeletonFrameReady;

                //Kinectセンサーからのストリーム取得を開始、以降、Kinectランタイムからフレーム毎に登録したColorFrameReadyメソッドが呼び出される
                kinect.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
                Close();
            }
        }

        /// <summary>
        /// 桁の補正
        /// </summary>
        /// <param name="date"></param>
        /// <returns></returns>
        public String digits(int date)
        {
            if (date / 10 == 0) return "0" + date;
            else return date.ToString();
        }
        
        /// <summary>
        /// Windowが閉じる時に呼び出されるイベント
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void WindowClosing(object sender, CancelEventArgs e)
        {
            rgbImage.Source = null;
            if(sw != null)sw.Close();

            if (kinect != null)
            {
                kinect = KinectSensor.KinectSensors[0];
                kinect.ColorStream.Disable();
                kinect.SkeletonStream.Disable();
            }
        }

        /// <summary>
        /// ColorFrameReady イベントハンドラ(画像情報を取得して描画)
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ColorImageReady(object sender, ColorImageFrameReadyEventArgs e)
        {
            //イベント引数からOpenColorImageFrameメソッドでイメージフレームのデータを受け取りimageFrame変数に保持
            //using文:IDisposableオブジェクト(.NETframeworkにおいて使用後に後始末が必要なオブジェクト)の正しい使用を保証する簡易構文
            //using(オブジェクト生成){処理}と書いておくとオブジェクトの処理後に後始末(Disposeメソッドの呼び出し)を保証
            using (ColorImageFrame imageFrame = e.OpenColorImageFrame())
            {
                if (imageFrame != null)
                {
                    
                    //画像情報の幅・高さ取得(途中で変わらない想定)
                    int frmWidth = imageFrame.Width;
                    int frmHeight = imageFrame.Height;

                    //画像情報をバッファにコピー
                    imageFrame.CopyPixelDataTo(pixelBuffer);

                    //ビットマップに描画
                    Int32Rect src = new Int32Rect(0, 0, frmWidth, frmHeight);
                    bmpBuffer.WritePixels(src, pixelBuffer, frmWidth * 4, 0);
                    if (RecordImage.IsChecked == true)
                    {
                        //bmpImage.Save(pathSaveFolder + "/bmp/" + StopWatch.E + ".bmp", System.Drawing.Imaging.ImageFormat.Bmp);
                    }
                }
            }
        }//ColorImageReady

        /// <summary>
        /// SkeletonFrameReady イベントハンドラ (スケルトン座標をあれこれ)
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SkeletonFrameReady(object sender, SkeletonFrameReadyEventArgs e)
        {
            //前フレームでの描画をクリア
            skeletonCanvas.Children.Clear();

            //スケルトンフレームのデータをskeletonFrame変数に保持
            using (SkeletonFrame skeletonFrame = e.OpenSkeletonFrame())
            {
                //スケルトンフレームがnullでないならそのまま
                if (skeletonFrame != null)
                {
                    //人数分のスケルトンデータ変数を配列で作る
                    Skeleton[] skeletonData = new Skeleton[skeletonFrame.SkeletonArrayLength];
                    //配列に各人物のスケルトンフレームのデータをコピー
                    skeletonFrame.CopySkeletonDataTo(skeletonData);

                    //スケルトンを認識した人数の数だけ繰り返す
                    foreach (var skeleton in skeletonData)
                    {
                        //その人物のスケルトンがTracked状態なら続ける
                        if (skeleton.TrackingState == SkeletonTrackingState.Tracked)
                        {
                            SkeletonToColor(skeleton.Joints[RecordJType]);
                            SkeletonRecog.Text = "Skeleton On";
                            if (RecordPoints.IsChecked == true)
                            {
                                StopWatch.Stop();
                                sw.WriteLine(StopWatch.Elapsed + ","
                                    + skeleton.Joints[RecordJType].Position.X + ","
                                    + skeleton.Joints[RecordJType].Position.Y + ","
                                    + skeleton.Joints[RecordJType].Position.Z);
                                StopWatch.Start();
                            }
                        }
                    }
                }
                else
                {
                    SkeletonRecog.Text = "Skeleton Off";
                }
            }
        }//skeletonFrameReady

        /// <summary>
        /// スケルトン座標をカラー座標系に変換して描画
        /// </summary>
        /// <param name="joint"></param>
        private void SkeletonToColor(Joint joint)
        {
            //スケルトン座標をカラー座標系に変換
            ColorImagePoint point = kinect.CoordinateMapper.MapSkeletonPointToColorPoint(joint.Position, kinect.ColorStream.Format);
            //ジョイント座標に印を描画
            skeletonCanvas.Children.Add(new Ellipse()
            {
                Margin = new Thickness(point.X - 40, point.Y - 40, 0, 0),
                Fill = new SolidColorBrush(Colors.Black),
                Width = 20,
                Height = 20,
            });

        }//SkeletonToColor

        /// <summary>
        /// RecordとSaveを同時にOnにするチェックボックス
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void AllCheck_Checked(object sender, RoutedEventArgs e)
        {
            RecordPoints.IsChecked = true;
            RecordImage.IsChecked = true;
        }

        /// <summary>
        /// 座標の記録チェック
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void RecordPoints_Checked(object sender, RoutedEventArgs e)
        {
            StopWatch.Start();
            //ファイル書き込み用のdirectoryを用意
            pathSaveFolder = pathKinect + "/" + dt.Year + digits(dt.Month) + digits(dt.Day) + digits(dt.Hour) + digits(dt.Minute) + "/";
            Directory.CreateDirectory(pathSaveFolder);
            //ファイルを用意
            sw = new StreamWriter(pathSaveFolder+"Points.csv", false);
        }

        /// <summary>
        /// 画像の保存チェック
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void RecordImage_Checked(object sender, RoutedEventArgs e)
        {

        }
    }//MainWindow
}//namespace