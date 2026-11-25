using MelodayMusic.Core.Audio;

namespace MelodayMusic.Core.Tests;

public class RobertaTokenizerTests
{
    private static RobertaTokenizer Shipped()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MelodayMusic.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        // Restored with the models by tools/engine/download.ps1, which the app build requires.
        return new RobertaTokenizer(Path.Combine(dir!.FullName, "src", "MelodayMusic.App", "Assets", "Engine", "tokenizer.json"));
    }

    // Ids from Hugging Face tokenizers 0.23.3 on the same tokenizer.json.
    [Theory]
    [InlineData("sea shanty music", new long[] { 0, 16466, 1481, 36488, 930, 2 })]
    [InlineData("a walking in the city song", new long[] { 0, 102, 3051, 11, 5, 343, 2214, 2 })]
    [InlineData("the sound of cafe", new long[] { 0, 627, 2369, 9, 16381, 2 })]
    [InlineData("xqzt", new long[] { 0, 1178, 1343, 329, 90, 2 })]
    public void Encodes_like_the_reference_tokenizer(string text, long[] expected)
    {
        var (ids, mask) = Shipped().Encode(text);
        Assert.Equal(RobertaTokenizer.Length, ids.Length);
        Assert.Equal(expected, ids.Take(expected.Length));
        Assert.All(ids.Skip(expected.Length), id => Assert.Equal(1, id));
        Assert.Equal(expected.Length, mask.Sum());
    }

    [Fact]
    public void Counts_how_many_pieces_a_word_takes()
    {
        var tokenizer = Shipped();
        Assert.Single(tokenizer.Tokens(" cafe"));
        Assert.Equal(2, tokenizer.Tokens(" polka").Count);
        Assert.Equal(4, tokenizer.Tokens(" xqzt").Count);
        Assert.Throws<InvalidDataException>(() => tokenizer.Encode(string.Join(' ', Enumerable.Repeat("music", 100))));
    }
}
