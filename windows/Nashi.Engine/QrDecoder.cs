namespace Nashi.Engine;

public static class QrDecoder
{
    public static string[] Decode(PixelImage image)
    {
        var reader=new ZXing.BarcodeReaderGeneric{AutoRotate=true,Options=new ZXing.Common.DecodingOptions{TryHarder=true,PossibleFormats=[ZXing.BarcodeFormat.QR_CODE]}};
        return reader.DecodeMultiple(image.Bgra,image.Width,image.Height,ZXing.RGBLuminanceSource.BitmapFormat.BGRA32)?.Select(r=>r.Text).Distinct().ToArray()??[];
    }
}
