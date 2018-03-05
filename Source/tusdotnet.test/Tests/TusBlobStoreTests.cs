using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Shouldly;
using tusdotnet.Models;
using tusdotnet.Models.Concatenation;
using tusdotnet.Stores;
using tusdotnet.test.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace tusdotnet.test.Tests
{
    public class TusBlobStoreTests : IClassFixture<TusBlobStoreFixture>
    {
        private readonly TusBlobStoreFixture _fixture;
        private readonly ITestOutputHelper _output;

        public TusBlobStoreTests(TusBlobStoreFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        [Fact]
        public async Task CreateFileAsync()
        {
            for (var i = 0; i < 10; i++)
            {
                var fileId = await _fixture.Store.CreateFileAsync(i, null, CancellationToken.None);
                
                fileId.ShouldNotBeNull();
            }
        }

        [Fact]
        public async Task FileExistsAsync()
        {
            for (var i = 0; i < 10; i++)
            {
                                
                var fileId = await _fixture.Store.CreateFileAsync(i, null, CancellationToken.None);
                var exist = await _fixture.Store.FileExistAsync(fileId, CancellationToken.None);

                exist.ShouldBeTrue();

                await _fixture.Store.DeleteFileAsync(fileId, CancellationToken.None);
            }

            for (var i = 0; i < 10; i++)
            {
                var exist = await _fixture.Store.FileExistAsync(Guid.NewGuid().ToString(), CancellationToken.None);
                exist.ShouldBeFalse();
            }


        }

        [Fact]
        public async Task GetUploadLengthAsync()
        {
            var fileId = await _fixture.Store.CreateFileAsync(3000, null, CancellationToken.None);
            var length = await _fixture.Store.GetUploadLengthAsync(fileId, CancellationToken.None);
            length.ShouldBe(3000);

            length = await _fixture.Store.GetUploadLengthAsync(Guid.NewGuid().ToString(), CancellationToken.None);
            length.ShouldBeNull();
            
        }

        [Fact]
        public async Task AppendDataAsync_Supports_Cancellation()
        {
            var cancellationToken = new CancellationTokenSource();

            // Test cancellation.

            // 5 MB
            const int fileSize = 5 * 1024 * 1024;
            var fileId = await _fixture.Store.CreateFileAsync(fileSize, null, cancellationToken.Token);

            var buffer = new MemoryStream(new byte[fileSize]);

            var appendTask = _fixture.Store
                .AppendDataAsync(fileId, buffer, cancellationToken.Token);

            await Task.Delay(150, CancellationToken.None);

            cancellationToken.Cancel();

            long bytesWritten = -1;

            try
            {
                bytesWritten = await appendTask;
                // Should have written something but should not have completed.
                bytesWritten.ShouldBeInRange(1, 10240000);
            }
            catch (TaskCanceledException)
            {
                // The Owin test server throws this exception instead of just disconnecting the client.
                // If this happens just ignore the error and verify that the file has been written properly below.
            }

            var fileOffset = await _fixture.Store.GetUploadOffsetAsync(fileId, CancellationToken.None);
            if (bytesWritten != -1)
            {
                fileOffset.ShouldBe(bytesWritten);
            }
            else
            {
                fileOffset.ShouldBeInRange(1, 10240000);
            }            
        }


        [Fact]
        public async Task AppendDataAsync_Throws_Exception_If_More_Data_Than_Upload_Length_Is_Provided()
        {
            // Test that it does not allow more than upload length to be written.

            var fileId = await _fixture.Store.CreateFileAsync(100, null, CancellationToken.None);

            var storeException = await Should.ThrowAsync<TusStoreException>(
                async () => await _fixture.Store.AppendDataAsync(fileId, new MemoryStream(new byte[101]), CancellationToken.None));

            storeException.Message.ShouldBe("Stream contains more data than the file's upload length. Stream data: 101, upload length: 100.");
        }

        [Fact]
        public async Task AppendDataAsync_Returns_Zero_If_File_Is_Already_Complete()
        {
            var fileId = await _fixture.Store.CreateFileAsync(100, null, CancellationToken.None);
            var length = await _fixture.Store.AppendDataAsync(fileId, new MemoryStream(new byte[100]), CancellationToken.None);
            length.ShouldBe(100);

            length = await _fixture.Store.AppendDataAsync(fileId, new MemoryStream(new byte[1]), CancellationToken.None);
            length.ShouldBe(0);
        }


        [Fact]
        public async Task GetFileAsync_Returns_File_If_The_File_Exist()
        {
            var fileId = await _fixture.Store.CreateFileAsync(100, null, CancellationToken.None);

            var content = Enumerable.Range(0, 100).Select(f => (byte)f).ToArray();

            await _fixture.Store.AppendDataAsync(fileId, new MemoryStream(content), CancellationToken.None);

            var file = await _fixture.Store.GetFileAsync(fileId, CancellationToken.None);

            file.Id.ShouldBe(fileId);

            using (var fileContent = await file.GetContentAsync(CancellationToken.None))
            {
                fileContent.Length.ShouldBe(content.Length);

                var fileContentBuffer = new byte[fileContent.Length];
                fileContent.Read(fileContentBuffer, 0, fileContentBuffer.Length);

                for (var i = 0; i < content.Length; i++)
                {
                    fileContentBuffer[i].ShouldBe(content[i]);
                }
            }
        }



        [Fact]
        public async Task GetFileAsync_Returns_Null_If_The_File_Does_Not_Exist()
        {
            var file = await _fixture.Store.GetFileAsync(Guid.NewGuid().ToString(), CancellationToken.None);
            file.ShouldBeNull();
        }

        [Fact]
        public async Task CreateFileAsync_Creates_Metadata_Properly()
        {
            var fileId = await _fixture.Store.CreateFileAsync(1, "key wrbDgMSaxafMsw==", CancellationToken.None);
            fileId.ShouldNotBeNull();

            var file = _fixture.Store.GetFileAsync(fileId, CancellationToken.None).Result;
                        
            var metadata = await file.GetMetadataAsync(CancellationToken.None);
            metadata.ContainsKey("key").ShouldBeTrue();
            // Correct encoding
            metadata["key"].GetString(new UTF8Encoding()).ShouldBe("¶ÀĚŧ̳");
            // Wrong encoding just to test that the result is different.
            metadata["key"].GetString(new UTF7Encoding()).ShouldBe("Â¶ÃÄÅ§Ì³");
            metadata["key"].GetBytes().ShouldBe(new byte[] { 194, 182, 195, 128, 196, 154, 197, 167, 204, 179 });
        }

        [Fact]
        public async Task GetUploadMetadataAsync()
        {
            const string metadataConst = "key wrbDgMSaxafMsw==";
            var fileId = await _fixture.Store.CreateFileAsync(1, metadataConst, CancellationToken.None);


            var metadata = await _fixture.Store.GetUploadMetadataAsync(fileId, CancellationToken.None);
            metadata.ShouldBe(metadataConst);

            fileId = await _fixture.Store.CreateFileAsync(1, null, CancellationToken.None);
            metadata = await _fixture.Store.GetUploadMetadataAsync(fileId, CancellationToken.None);
            metadata.ShouldBeNullOrEmpty();

            fileId = await _fixture.Store.CreateFileAsync(1, "", CancellationToken.None);
            metadata = await _fixture.Store.GetUploadMetadataAsync(fileId, CancellationToken.None);
            metadata.ShouldBeNullOrEmpty();
        }

        [Fact]
        public async Task DeleteFileAsync()
        {
            const string metadataConst = "key wrbDgMSaxafMsw==";
            for (var i = 0; i < 10; i++)
            {
                var fileId = await _fixture.Store.CreateFileAsync(i + 1, i % 2 == 0 ? null : metadataConst, CancellationToken.None);
                var exist = await _fixture.Store.FileExistAsync(fileId, CancellationToken.None);
                exist.ShouldBeTrue();

                await _fixture.Store.DeleteFileAsync(fileId, CancellationToken.None).ContinueWith(async t =>
                {
                   var exists = await _fixture.Store.FileExistAsync(fileId, CancellationToken.None);
                   exists.ShouldBeFalse();
                });
                
            }
        }


    }

    // ReSharper disable once ClassNeverInstantiated.Global - Instantiated by xUnit.
    public class TusBlobStoreFixture : IDisposable
    {
        
        public TusBlobStore Store { get;}

        public TusBlobStoreFixture()
        {            
            Store = new TusBlobStore("DefaultEndpointsProtocol=https;AccountName=foliownstore;AccountKey=j4kisxBc/VWV3jkaliUx1jpBXS+gGdmbDRIydeQA3gzgn4bzWmJoz/uE8cwEORc4fW7hAQ+wvAjv9VUSw9C/nw==;EndpointSuffix=core.windows.net","tustests");
        }

        public void Dispose()
        {

        }
    }


}
