using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Diagnostics;
var path = args[0];
using var options = new SessionOptions { IntraOpNumThreads = 1, InterOpNumThreads = 1, ExecutionMode = ExecutionMode.ORT_SEQUENTIAL };
using var model = new InferenceSession(path, options);
float[] Run(float[] input) {
 using var result = model.Run(new[] { NamedOnnxValue.CreateFromTensor("waveform", new DenseTensor<float>(input, new[] { 1, 480000 })) });
 var vector = result.First().AsTensor<float>().ToArray();
 if(vector.Length != 512 || vector.Any(v => !float.IsFinite(v))) throw new Exception("Invalid embedding");
 return vector;
}
var tone = Enumerable.Range(0,480000).Select(i => (float)(0.2*Math.Sin(2*Math.PI*440*i/48000))).ToArray();
var random = new Random(123); var noise = Enumerable.Range(0,480000).Select(_ => (float)(random.NextDouble()*0.4-0.2)).ToArray();
var timer = Stopwatch.StartNew();
var a = Run(tone); var b = Run(tone); var c = Run(noise);
double Cos(float[] x,float[] y) => x.Zip(y,(l,r)=>(double)l*r).Sum()/Math.Sqrt(x.Sum(v=>(double)v*v)*y.Sum(v=>(double)v*v));
if(Cos(a,b)<0.99999 || Cos(a,c)>0.999) throw new Exception("Embedding smoke test failed");
Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { Dimensions=a.Length, RepeatCosine=Cos(a,b), DifferentSignalCosine=Cos(a,c), ThreeInferencesMs=timer.ElapsedMilliseconds, WorkingSetMb=Environment.WorkingSet/1048576 }));
