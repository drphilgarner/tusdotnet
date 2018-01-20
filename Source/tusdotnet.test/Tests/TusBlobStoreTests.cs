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
