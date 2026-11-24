using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MagicMusicWall.App;
using MagicMusicWall.Core;
class Program {
static readonly string Root=System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MagicMusicWall-DetailsChecks-"+Guid.NewGuid().ToString("N"));
static string Images => Root;
const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
static IEnumerable<DependencyObject> Walk(DependencyObject d){for(int i=0;i<VisualTreeHelper.GetChildrenCount(d);i++){var c=VisualTreeHelper.GetChild(d,i);yield return c;foreach(var n in Walk(c))yield return n;}}
static void Shot(Window w,string name){w.UpdateLayout();var bmp=new RenderTargetBitmap((int)w.ActualWidth,(int)w.ActualHeight,96,96,PixelFormats.Pbgra32);bmp.Render(w);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bmp));using var f=System.IO.File.Create(Images+"/"+name+".png");encoder.Save(f);}
[STAThread]static int Main(){System.IO.Directory.CreateDirectory(Root);typeof(App).GetProperty(nameof(App.DataDirectory))!.SetValue(null,Root);Exception? failure=null;var app=new App();app.InitializeComponent();app.Startup+=(_,_)=>app.Dispatcher.BeginInvoke(async()=>{var main=(MainWindow)app.MainWindow;Window? song=null;Window? setupWindow=null;try{
 await Task.Delay(300);
 var setupType=typeof(MainWindow).Assembly.GetType("MagicMusicWall.App.SetupWindow")!;
 var setup=(Window)Activator.CreateInstance(setupType,BindingFlags.Instance|BindingFlags.NonPublic,null,[main],null)!;
 setupWindow=setup;setup.Owner=main;setup.Show();await Task.Delay(200);
 var dirty=setupType.GetField("_dirty",F)!;dirty.SetValue(setup,false);
 var tabs=(TabControl)setup.FindName("SettingsTabs");tabs.SelectedIndex=0;await Task.Delay(100);tabs.SelectedIndex=2;await Task.Delay(100);
 Check(!(bool)dirty.GetValue(setup)!,"Tab browsing marks settings dirty");
 ((TextBox)setup.FindName("RemoteAddressLink")).Text="Demonstration address";Check(!(bool)dirty.GetValue(setup)!,"Read-only display marks settings dirty");
 ((Slider)setup.FindName("Volume")).Value=23;Check((bool)dirty.GetValue(setup)!,"Volume edit fails to mark settings dirty");
 dirty.SetValue(setup,false);setup.Close();

 var type=typeof(MainWindow).Assembly.GetType("MagicMusicWall.App.SongInfoWindow")!;
 var item=new MediaItem{Title="First Light",Artist="The Example Quartet",Album="Evening Sessions",Genre="Jazz",Year=2026,DurationSeconds=240,Path=Root+"/First Light.wav",MusicBrainz=new(){Title="First Light",Artist="The Example Quartet",Album="Evening Sessions",Genres=["Jazz"],Instruments=["piano","double bass","drums"],Credits=["Demonstration metadata - not a real MusicBrainz record."],RetrievedUtc=DateTimeOffset.UtcNow}};
 song=(Window)Activator.CreateInstance(type,[item,new MusicLookupService(),new SongFactsService(),(Action<RecordingMatch?>)(_=>{}),(Action<Window>)(_=>{}),(Action<Window>)(_=>{}),(Action<Window>)(_=>{})])!;
 song.Show();song.Width=960;song.Height=900;await Task.Delay(300);type.GetMethod("SelectTab",F)!.Invoke(song,[3]);await Task.Delay(100);Shot(song,"song-details");
 type.GetMethod("SelectTab",F)!.Invoke(song,[2]);
 var article=(TextBox)type.GetField("_article",F)!.GetValue(song)!;
 article.Text="First Light - demonstration article\n\nThis sample text shows the reading layout using fictional music. No online article or real artist data was used.\n\nThe quartet recorded the piece in a single afternoon, with piano carrying the melody over a gentle bass line and brushed drums. The arrangement leaves space for each instrument, while returning to the opening theme between solos.\n\nUse the divider above this reader to give more room to the article or its search results. Double-click a matching result to load its excerpt, then follow Read full article for the complete source and references.\n\n"+string.Join("\n\n",Enumerable.Repeat("Additional demonstration paragraph showing scrollable reading space for background, credits and recording history.",20));
 var results=(ListBox)type.GetField("_articles",F)!.GetValue(song)!;
 results.Items.Add(new ListBoxItem{Content="First Light - demonstration song article"}); results.Items.Add(new ListBoxItem{Content="Evening Sessions - demonstration album article"}); results.SelectedIndex=0;
 await Task.Delay(200);song.UpdateLayout(); Check(article.ActualHeight>=300,"Article reader too short: "+article.ActualHeight); Shot(song,"article-reader");
 var large=article.ActualHeight; song.Width=620;song.Height=520;await Task.Delay(200);song.UpdateLayout();var page=(ScrollViewer)song.Content;
 Check(page.ScrollableHeight>0,"Short window cannot scroll");Check(article.ActualHeight>=180,"Small reader collapsed");article.ScrollToEnd();await Task.Delay(100);Check(article.VerticalOffset>0,"Reader cannot scroll");
 Check(Walk(song).OfType<GridSplitter>().Any(),"Missing article divider");
 System.IO.File.WriteAllText(Root+"/result.json",System.Text.Json.JsonSerializer.Serialize(new{success=true,largeArticleHeight=large,smallArticleHeight=article.ActualHeight,outerScroll=page.ScrollableHeight}));
 }catch(Exception ex){failure=ex;System.IO.File.WriteAllText(Root+"/result.json",ex.ToString());}finally{if(setupWindow is not null){setupWindow.GetType().GetField("_dirty",F)!.SetValue(setupWindow,false);setupWindow.Close();}song?.Close();main.Close();}});app.Run();Console.WriteLine(failure is null ? "Details checks passed. Screenshots: "+Root : failure.ToString());return failure is null?0:1;}}
