// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information



using Microsoft.AspNetCore.WebHooks.Storage;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.WebHooks
{
    // Modified version from here: https://github.com/aspnet/AspNetWebHooks/blob/master/src/Microsoft.AspNet.WebHooks.Custom.SqlStorage/WebHooks/DbWebHookStore.cs
    /// <summary>
    /// Provides an abstract implementation of <see cref="IWebHookStore"/> targeting SQL using a parameterized <see cref="DbContext"/>.
    /// The <see cref="DbContext"/> must contain an entity of type <see cref="IRegistration"/> as this is used to access the data
    /// in the DB.
    /// </summary>
    /// <typeparam name="TContext">The type of <see cref="DbContext"/> to be used.</typeparam>
    /// <typeparam name="TRegistration">The type of <see cref="IRegistration"/> to be used.</typeparam>    
    public abstract class DbWebHookStore<TContext, TRegistration> : WebHookStore
        where TContext : DbContext, new()
        where TRegistration : class, IRegistration, new()
    {
        private readonly JsonSerializerSettings _serializerSettings = new JsonSerializerSettings() { Formatting = Formatting.None };
        
        /// <summary>
        /// Initializes a new instance of the <see cref="DbWebHookStore{TContext,TRegistration}"/> class with the given <paramref name="logger"/>.
        /// Using this constructor, the data will not be encrypted while persisted to the database.
        /// </summary>
        protected DbWebHookStore()
        {
           
        }
       

        /// <inheritdoc />
        public override async Task<ICollection<WebHook>> GetAllWebHooksAsync(string user)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }

            user = NormalizeKey(user);

            try
            {
                using (var context = GetContext())
                {
                    var registrations = await context.Set<TRegistration>().Where(r => r.User == user).ToArrayAsync();
                    ICollection<WebHook> result = registrations.Select(r => ConvertToWebHook(r))
                        .Where(w => w != null)
                        .ToArray();
                    return result;
                }
            }
            catch (Exception ex)
            {
                var message = $"GetAllWebHooksAsync failed. Exception: {ex.Message}";
                //_logger.Error(message, ex);
                throw new InvalidOperationException(message, ex);
            }
        }

        /// <inheritdoc />
        public override async Task<ICollection<WebHook>> QueryWebHooksAsync(string user, IEnumerable<string> actions, Func<WebHook, string, bool> predicate)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }

            user = NormalizeKey(user);

            predicate = predicate ?? DefaultPredicate;

            try
            {
                using (var context = GetContext())
                {
                    var registrations = await context.Set<TRegistration>().Where(r => r.User == user).ToArrayAsync();
                    ICollection<WebHook> matches = registrations.Select(r => ConvertToWebHook(r))
                        .Where(w => MatchesAnyAction(w, actions) && predicate(w, user))
                        .ToArray();
                    return matches;
                }
            }
            catch (Exception ex)
            {
                var message = $"QueryWebHooksAsync failed. Exception: {ex.Message}";
                throw new InvalidOperationException(message, ex);
            }
        }

        /// <inheritdoc />
        public override async Task<WebHook> LookupWebHookAsync(string user, string id)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }
            if (id == null)
            {
                throw new ArgumentNullException(nameof(id));
            }

            user = NormalizeKey(user);
            id = NormalizeKey(id);

            try
            {
                using (var context = GetContext())
                {
                    var registration = await context.Set<TRegistration>().Where(r => r.User == user && r.Id == id).FirstOrDefaultAsync();
                    if (registration != null)
                    {
                        return ConvertToWebHook(registration);
                    }
                    return null;
                }
            }
            catch (Exception ex)
            {
                var message = $"LookupWebHookAsync failed. Exception: {ex.Message}";
                throw new InvalidOperationException(message, ex);
            }
        }

        /// <inheritdoc />
        public override async Task<StoreResult> InsertWebHookAsync(string user, WebHook webHook)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }
            if (webHook == null)
            {
                throw new ArgumentNullException(nameof(webHook));
            }

            user = NormalizeKey(user);

            try
            {
                using (var context = GetContext())
                {
                    var registration = ConvertFromWebHook(user, webHook);
                    context.Set<TRegistration>().Attach(registration);
                    context.Entry(registration).State = EntityState.Added;
                    await context.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                var message = $"InsertWebHookAsync failed. Exception: {ex.Message}";                
                return StoreResult.InternalError;
            }
            return StoreResult.Success;
        }

        /// <inheritdoc />
        public override async Task<StoreResult> UpdateWebHookAsync(string user, WebHook webHook)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }
            if (webHook == null)
            {
                throw new ArgumentNullException(nameof(webHook));
            }

            user = NormalizeKey(user);

            try
            {
                using (var context = GetContext())
                {
                    var registration = await context.Set<TRegistration>().Where(r => r.User == user && r.Id == webHook.Id).FirstOrDefaultAsync();
                    if (registration == null)
                    {
                        return StoreResult.NotFound;
                    }
                    UpdateRegistrationFromWebHook(user, webHook, registration);
                    context.Entry(registration).State = EntityState.Modified;
                    await context.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                var message = $"UpdateWebHookAsync failed. Exception: {ex.Message}";
                return StoreResult.InternalError;
            }
            return StoreResult.Success;
        }

        /// <inheritdoc />
        public override async Task<StoreResult> DeleteWebHookAsync(string user, string id)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }
            if (id == null)
            {
                throw new ArgumentNullException(nameof(id));
            }

            user = NormalizeKey(user);

            try
            {
                using (var context = GetContext())
                {
                    var match = await context.Set<TRegistration>().Where(r => r.User == user && r.Id == id).FirstOrDefaultAsync();
                    if (match == null)
                    {
                        return StoreResult.NotFound;
                    }
                    context.Entry(match).State = EntityState.Deleted;
                    await context.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                var message = $"DeleteWebHookAsync failed. Exception: {ex.Message}";               
                return StoreResult.InternalError;
            }
            return StoreResult.Success;
        }

        /// <inheritdoc />
        public override async Task DeleteAllWebHooksAsync(string user)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }

            user = NormalizeKey(user);

            try
            {
                using (var context = GetContext())
                {
                    var matches = await context.Set<TRegistration>().Where(r => r.User == user).ToArrayAsync();
                    foreach (var m in matches)
                    {
                        context.Entry(m).State = EntityState.Deleted;
                    }
                    await context.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                var message = $"DeleteAllWebHooksAsync failed. Exception: {ex.Message}";
                throw new InvalidOperationException(message, ex);
            }
        }

        /// <inheritdoc />
        public override async Task<ICollection<WebHook>> QueryWebHooksAcrossAllUsersAsync(IEnumerable<string> actions, Func<WebHook, string, bool> predicate)
        {
            if (actions == null)
            {
                throw new ArgumentNullException(nameof(actions));
            }

            predicate = predicate ?? DefaultPredicate;

            try
            {
                using (var context = GetContext())
                {
                    var registrations = await context.Set<TRegistration>().ToArrayAsync();
                    var matches = new List<WebHook>();
                    foreach (var registration in registrations)
                    {
                        var webHook = ConvertToWebHook(registration);
                        if (MatchesAnyAction(webHook, actions) && predicate(webHook, registration.User))
                        {
                            matches.Add(webHook);
                        }
                    }
                    return matches;
                }
            }
            catch (Exception ex)
            {
                var message = $"QueryWebHooksAcrossAllUsersAsync failed. Exception: {ex.Message}";
                throw new InvalidOperationException(message, ex);
            }
        }

        /// <summary>
        /// Converts the provided <paramref name="registration"/> to a <see cref="WebHook"/> instance
        /// which is returned from this <see cref="IWebHookStore"/> implementation.
        /// </summary>
        /// <param name="registration">The instance to convert.</param>
        /// <returns>An initialized <see cref="WebHook"/> instance.</returns>
        protected virtual WebHook ConvertToWebHook(TRegistration registration)
        {
            if (registration == null)
            {
                return null;
            }

            try
            {
                var content = registration.ProtectedData;
                var webHook = JsonConvert.DeserializeObject<WebHook>(content, _serializerSettings);
                return webHook;
            }
            catch (Exception ex)
            {
                var message = $"Bad WebHook {typeof(WebHook).Name}. Exception: {ex.Message}";
                //_logger.Error(message, ex);
            }
            return null;
        }

        /// <summary>
        /// Converts the provided <paramref name="webHook"/> associated with the given
        /// <paramref name="user"/> to an <typeparamref name="TRegistration"/> instance
        /// which is used by this <see cref="IWebHookStore"/> implementation.
        /// </summary>
        /// <param name="user">The user associated with this <see cref="WebHook"/>.</param>
        /// <param name="webHook">The <see cref="WebHook"/> to convert.</param>
        /// <returns>An initialized <typeparamref name="TRegistration"/> instance.</returns>
        protected virtual TRegistration ConvertFromWebHook(string user, WebHook webHook)
        {
            if (webHook == null)
            {
                throw new ArgumentNullException(nameof(webHook));
            }

            var content = JsonConvert.SerializeObject(webHook, _serializerSettings);
            var protectedData = content;
            var registration = new TRegistration
            {
                User = user,
                Id = webHook.Id,
                ProtectedData = protectedData
            };
            return registration;
        }

        /// <summary>
        /// Updates an existing <typeparamref name="TRegistration"/> instance with data provided
        /// by the given <paramref name="webHook"/>.
        /// </summary>
        /// <param name="user">The user associated with this <see cref="WebHook"/>.</param>
        /// <param name="webHook">The <paramref name="webHook"/> to update the existing <paramref name="registration"/> with.</param>
        /// <param name="registration">The <typeparamref name="TRegistration"/> instance to update.</param>
        protected virtual void UpdateRegistrationFromWebHook(string user, WebHook webHook, TRegistration registration)
        {
            if (webHook == null)
            {
                throw new ArgumentNullException(nameof(webHook));
            }
            if (registration == null)
            {
                throw new ArgumentNullException(nameof(registration));
            }

            registration.User = user;
            registration.Id = webHook.Id;
            var content = JsonConvert.SerializeObject(webHook, _serializerSettings);
            var protectedData = content;
            registration.ProtectedData = protectedData;
        }

        /// <summary>
        /// Constructs a new context instance
        /// </summary>
        protected virtual TContext GetContext()
        {
            return new TContext();
        }

        private static bool DefaultPredicate(WebHook webHook, string user)
        {
            return true;
        }
    }
}
