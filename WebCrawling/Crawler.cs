﻿using CsQuery;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.Caching;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive;
using System.Reactive.Disposables;

namespace MisterHex.WebCrawling
{
    public class Crawler
    {
        public static IObservable<Uri> Crawl(Uri uri)
        {
            return new Crawler().StartCrawl(uri);
        }

        private List<Uri> _jobList = new List<Uri>();
        private ReplaySubject<Uri> _subject = new ReplaySubject<Uri>();

        private IObservable<Uri> StartCrawl(Uri uri)
        {
            StartCrawlingAsync(uri);
            return _subject;
        }

        private Task StartCrawlingAsync(Uri uri)
        {
            return Task.Factory.StartNew(() => StartCrawling(uri));
        }

        private void StartCrawling(Uri uri)
        {
            _jobList.Add(uri);
            IUriFilter[] filterers = GetFilters(uri).ToArray();

            while (_jobList.Count != 0)
            {
                List<Task<IEnumerable<Uri>>> crawlerTasks = new List<Task<IEnumerable<Uri>>>();

                var jobsToRun = _jobList.ToList();
                _jobList.Clear();

                jobsToRun.ForEach(i =>
                    {
                        var task = CrawlSingle(i)
                            .ContinueWith(t =>
                                {
                                    var filtered = Filter(t.Result, filterers).AsEnumerable();
                                    filtered.ToList().ForEach(_subject.OnNext);
                                    return filtered;
                                }, TaskContinuationOptions.OnlyOnRanToCompletion);
                        crawlerTasks.Add(task);
                    });

                Task.WaitAll(crawlerTasks.ToArray());

                List<Uri> newLinks = crawlerTasks.SelectMany(i => i.Result).ToList();

                _jobList.AddRange(newLinks.ToArray());
            }

            _subject.OnCompleted();
        }


        public static async Task<IEnumerable<Uri>> CrawlSingle(Uri uri)
        {
            using (HttpClient client = new HttpClient() { Timeout = TimeSpan.FromMinutes(1) })
            {
                IEnumerable<Uri> result = new List<Uri>();

                try
                {
                    string html = await client.GetStringAsync(uri).ContinueWith(t => t.Result, TaskContinuationOptions.OnlyOnRanToCompletion);
                    result = CQ.Create(html)["a"].Select(i => i.Attributes["href"]).SafeSelect(i => new Uri(i));
                    return result;
                }
                catch
                { }
                return result;
            }
        }

        private static List<Uri> Filter(IEnumerable<Uri> uris, params IUriFilter[] filters)
        {
            var filtered = uris.ToList();
            foreach (var filter in filters.ToList())
            {
                filtered = filter.Filter(filtered);
            }
            return filtered;
        }

        private static List<IUriFilter> GetFilters(Uri uri)
        {
            return new List<IUriFilter>() 
            {
                new ExcludeRootUriFilter(uri), 
                new ExternalUriFilter(uri), 
                new AlreadyVisitedUriFilter()
            };
        }

    }
}