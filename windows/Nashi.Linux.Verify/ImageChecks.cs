using System.Security.Cryptography;
using ImageMagick;
using Nashi.Core;
using Nashi.Engine;
namespace Nashi.Linux.Verify;

internal static partial class Program
{
    private static string Sha(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    static partial void ImageChecks()
    {
        _=PhotoCodec.Format("png");
        var dir=Path.Combine(Path.GetTempPath(),"nashi-image-checks-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        try
        {
            string Fixture(string name,uint w,uint h,Action<MagickImage>? tweak=null){var path=Path.Combine(dir,name);using var image=new MagickImage(MagickColors.SteelBlue,w,h);tweak?.Invoke(image);image.Write(path);return path;}
            // Round trips keep dimensions in every raster format.
            foreach(var ext in new[]{"png","jpg","webp","tiff"})
            {
                var source=Fixture("rt."+ext,64,48);var before=Sha(source);var loaded=ImageEngine.Load(source);
                Check(loaded.Width==64&&loaded.Height==48,"load "+ext);
                var saved=Path.Combine(dir,"saved-"+ext+"."+ext);ImageEngine.Save(loaded,saved,ext);var again=ImageEngine.Load(saved);
                Check(again.Width==64&&again.Height==48,"round trip "+ext);
                var written=ImageEngine.Apply("convert",[source],Path.Combine(dir,"out-rt-"+ext),new ImageOptions(),ext);
                var result=ImageEngine.Load(written[0]);Check(result.Width==64&&result.Height==48&&Sha(source)==before,"convert "+ext);
            }
            var png=Fixture("a.png",100,60);var pngHash=Sha(png);
            var resized=ImageEngine.Load(ImageEngine.Apply("resize",[png],Path.Combine(dir,"o-resize"),new ImageOptions(Width:50,Height:20))[0]);
            Check(resized.Width==50&&resized.Height==20,"resize exact");Check(Sha(png)==pngHash,"resize keeps input");
            var byWidth=ImageEngine.Load(ImageEngine.Apply("resize",[png],Path.Combine(dir,"o-resize2"),new ImageOptions(Width:50))[0]);
            Check(byWidth.Width==50&&byWidth.Height==30,"resize width keeps aspect");
            var rotated=ImageEngine.Load(ImageEngine.Apply("rotate",[png],Path.Combine(dir,"o-rotate"),new ImageOptions(Rotation:90))[0]);
            Check(rotated.Width==60&&rotated.Height==100,"rotate 90 swaps");Check(Sha(png)==pngHash,"rotate keeps input");
            var cropped=ImageEngine.Load(ImageEngine.Apply("crop",[png],Path.Combine(dir,"o-crop"),new ImageOptions(Crop:new PixelRect(10,5,30,20)))[0]);
            Check(cropped.Width==30&&cropped.Height==20,"crop size");Check(Sha(png)==pngHash,"crop keeps input");
            var flipped=ImageEngine.Load(ImageEngine.Apply("flip",[png],Path.Combine(dir,"o-flip"),new ImageOptions())[0]);
            Check(flipped.Width==100&&flipped.Height==60,"flip size");
            var edited=ImageEngine.Load(ImageEngine.Apply("editImage",[png],Path.Combine(dir,"o-edit"),new ImageOptions{Adjustments=new PhotoAdjustments{Brightness=.1}})[0]);
            Check(edited.Width==100&&edited.Height==60,"editImage size");Check(Sha(png)==pngHash,"editImage keeps input");
            // EXIF orientation 6 (rotate 90 clockwise) turns a landscape JPEG portrait.
            var exif=Fixture("exif.jpg",40,20,image=>{var profile=new ExifProfile();profile.SetValue(ExifTag.Orientation,(ushort)6);image.SetProfile(profile);image.Orientation=Orientation.RightTop;});
            var oriented=ImageEngine.Load(exif);Check(oriented.Width==20&&oriented.Height==40,"EXIF orientation applied");
            // Transparent PNG flattens on white in JPEG.
            var clear=Fixture("clear.png",16,16,image=>{image.HasAlpha=true;image.Evaluate(Channels.Alpha,EvaluateOperator.Set,0);});var clearHash=Sha(clear);
            var flat=ImageEngine.Load(ImageEngine.Apply("convert",[clear],Path.Combine(dir,"o-flat"),new ImageOptions(),"jpg")[0]);
            Check(flat.Bgra[0]>=250&&flat.Bgra[1]>=250&&flat.Bgra[2]>=250&&flat.Bgra[3]==255,"transparent PNG flattened on white");Check(Sha(clear)==clearHash,"flatten keeps input");
            // Output naming never overwrites.
            var named=Path.Combine(dir,"o-name");Directory.CreateDirectory(named);var existing=Path.Combine(named,"a.jpg");File.WriteAllText(existing,"keep");
            var first=ImageEngine.Apply("convert",[png],named,new ImageOptions(),"jpg");var second=ImageEngine.Apply("convert",[png],named,new ImageOptions(),"jpg");
            Check(File.ReadAllText(existing)=="keep"&&first[0]!=existing&&second[0]!=first[0]&&File.Exists(second[0]),"no overwrite");
            Check(Path.GetFileName(first[0])=="a 2.jpg","collision naming");Check(Sha(png)==pngHash,"convert keeps input");
            var threw=false;try{ImageEngine.Apply("compress",[png],Path.Combine(dir,"o-x"),new ImageOptions());}catch(NotSupportedException error){threw=error.Message=="compress is not available on Linux yet.";}
            Check(threw,"deferred tool throws NotSupported");
            Check(ImageEngine.FromPng(ImageEngine.PngBytes(resized)) is {Width:50,Height:20},"PNG bytes round trip");
        }
        finally{try{Directory.Delete(dir,true);}catch(IOException){}}
    }
}
