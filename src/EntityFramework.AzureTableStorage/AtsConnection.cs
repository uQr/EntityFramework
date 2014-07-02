﻿// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.Data.Entity.AzureTableStorage.Requests;
using Microsoft.Data.Entity.AzureTableStorage.Utilities;
using Microsoft.Data.Entity.Infrastructure;
using Microsoft.Data.Entity.Storage;
using Microsoft.Data.Entity.Utilities;
using Microsoft.Framework.Logging;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Data.Entity.AzureTableStorage
{
    public class AtsConnection : DataStoreConnection
    {
        private readonly ThreadSafeLazyRef<CloudStorageAccount> _account;
        private readonly ThreadSafeLazyRef<CloudTableClient> _tableClient;
        private bool _batching;

        /// <summary>
        ///     For testing. Improper usage may lead to NullReference exceptions
        /// </summary>
        internal AtsConnection()
        {
        }

        public AtsConnection([NotNull] DbContextConfiguration configuration)
        {
            Check.NotNull(configuration, "configuration");

            var storeConfig = configuration
                .ContextOptions
                .Extensions
                .OfType<AtsOptionsExtension>()
                .Single();

            var connectionString = storeConfig.ConnectionString;
            _batching = storeConfig.UseBatching;

            _account = new ThreadSafeLazyRef<CloudStorageAccount>(() => CloudStorageAccount.Parse(connectionString));
            _tableClient = new ThreadSafeLazyRef<CloudTableClient>(() => _account.Value.CreateCloudTableClient());
        }

        public virtual bool Batching
        {
            get { return _batching; }
            internal set { _batching = value; }
        }

        public virtual CloudStorageAccount Account
        {
            get { return _account.Value; }
        }

        public virtual TResult ExecuteRequest<TResult>([NotNull] AtsRequest<TResult> request, [CanBeNull] ILogger logger = null)
        {
            Check.NotNull(request, "request");

            var requestContext = SetupRequestContext(request, logger);
            return request.Execute(requestContext);
        }

        public virtual Task<TResult> ExecuteRequestAsync<TResult>([NotNull] AtsAsyncRequest<TResult> request, [CanBeNull] ILogger logger = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            Check.NotNull(request, "request");

            var requestContext = SetupRequestContext(request, logger);
            return request.ExecuteAsync(requestContext, cancellationToken);
        }

        private RequestContext SetupRequestContext<TResult>(AtsRequest<TResult> request, ILogger logger = null)
        {
            var operationContext = new OperationContext();
            if (logger != null)
            {
                operationContext.Retrying += (sender, args) => logger.WriteInformation(args.Request.RequestUri.ToString());

                operationContext.SendingRequest += (sender, args) => logger.WriteVerbose(args.Request.RequestUri.ToString());
                operationContext.ResponseReceived += (sender, args) =>
                    {
                        var msg = String.Format("{0} {1}", args.Response.StatusCode, args.Response.StatusDescription);
                        if (args.Response.StatusCode >= HttpStatusCode.BadRequest)
                        {
                            logger.WriteError(msg);
                        }
                        else
                        {
                            logger.WriteVerbose(msg);
                        }
                    };
                logger.WriteInformation("Executing request: " + request.Name);
            }
            return new RequestContext
                {
                    OperationContext = operationContext,
                    TableClient = _tableClient.Value,
                };
        }
    }
}
