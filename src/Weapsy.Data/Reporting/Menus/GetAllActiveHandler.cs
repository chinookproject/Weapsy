﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AutoMapper;
using Weapsy.Domain.Languages;
using Weapsy.Domain.Menus;
using Weapsy.Infrastructure.Caching;
using Weapsy.Infrastructure.Queries;
using Weapsy.Reporting.Menus;
using Weapsy.Reporting.Menus.Queries;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Weapsy.Data.Identity;
using Weapsy.Domain.Pages;
using Language = Weapsy.Data.Entities.Language;
using Menu = Weapsy.Data.Entities.Menu;
using MenuItem = Weapsy.Data.Entities.MenuItem;

namespace Weapsy.Data.Reporting.Menus
{
    public class GetViewModelHandler : IQueryHandlerAsync<GetViewModel, MenuViewModel>
    {
        private readonly IDbContextFactory _contextFactory;
        private readonly IMapper _mapper;
        private readonly ICacheManager _cacheManager;
        private readonly IRoleService _roleService;

        public GetViewModelHandler(IDbContextFactory contextFactory, IMapper mapper, ICacheManager cacheManager, IRoleService roleService)
        {
            _contextFactory = contextFactory;
            _mapper = mapper;
            _cacheManager = cacheManager;
            _roleService = roleService;
        }

        public async Task<MenuViewModel> RetrieveAsync(GetViewModel query)
        {
            return _cacheManager.Get(string.Format(CacheKeys.MenuCacheKey, query.SiteId, query.Name, query.LanguageId), () =>
            {
                using (var context = _contextFactory.Create())
                {
                    var menu = context.Menus.FirstOrDefault(x => x.SiteId == query.SiteId && x.Name == query.Name && x.Status != MenuStatus.Deleted);

                    if (menu == null)
                        return new MenuViewModel();

                    LoadMenuItems(context, menu);

                    var language = query.LanguageId != Guid.Empty
                        ? context.Languages.FirstOrDefault(x => x.SiteId == query.SiteId && x.Id == query.LanguageId && x.Status == LanguageStatus.Active)
                        : null;

                    bool addLanguageSlug = context.Sites.Where(x => x.Id == query.SiteId).Select(site => site.AddLanguageSlug).FirstOrDefault();

                    var menuModel = new MenuViewModel
                    {
                        Name = menu.Name,
                        MenuItems = PopulateMenuItems(context, menu.MenuItems, Guid.Empty, language, addLanguageSlug)
                    };

                    return menuModel;
                }
            });
        }

        private List<MenuViewModel.MenuItem> PopulateMenuItems(WeapsyDbContext context, IEnumerable<Entities.MenuItem> source, Guid parentId, Language language, bool addLanguageSlug)
        {
            var result = new List<MenuViewModel.MenuItem>();

            var menuItems = source as IList<MenuItem> ?? source.ToList();

            foreach (var menuItem in menuItems.Where(x => x.ParentId == parentId).OrderBy(x => x.SortOrder).ToList())
            {
                var menuItemRoleIds = menuItem.MenuItemPermissions.Select(x => x.RoleId);

                var text = menuItem.Text;
                var title = menuItem.Title;
                var url = "#";

                if (language != null)
                {
                    var menuItemLocalisation = menuItem.MenuItemLocalisations.FirstOrDefault(x => x.LanguageId == language.Id);

                    if (menuItemLocalisation != null)
                    {
                        text = !string.IsNullOrWhiteSpace(menuItemLocalisation.Text) ? menuItemLocalisation.Text : text;
                        title = !string.IsNullOrWhiteSpace(menuItemLocalisation.Title) ? menuItemLocalisation.Title : title;
                    }
                }

                if (menuItem.Type == MenuItemType.Page)
                {
                    var page = context.Pages
                        .Include(x => x.PageLocalisations)
                        .Include(x => x.PagePermissions)
                        .FirstOrDefault(x => x.Id == menuItem.PageId && x.Status != PageStatus.Deleted);

                    if (page == null)
                        continue;

                    if (language == null)
                    {
                        url = $"/{page.Url}";
                    }
                    else
                    {
                        var pageLocalisation = page.PageLocalisations.FirstOrDefault(x => x.LanguageId == language.Id);

                        url = pageLocalisation == null
                            ? $"/{page.Url}"
                            : !string.IsNullOrEmpty(pageLocalisation.Url)
                                ? $"/{pageLocalisation.Url}"
                                : $"/{page.Url}";

                        if (addLanguageSlug)
                            url = $"/{language.Url}" + url;
                    }

                    menuItemRoleIds = page.PagePermissions.Where(x => x.Type == PermissionType.View).Select(x => x.RoleId);
                }
                else if (menuItem.Type == MenuItemType.Link && !string.IsNullOrWhiteSpace(menuItem.Link))
                {
                    url = menuItem.Link;
                }

                var menuItemRoles = _roleService.GetRolesFromIds(menuItemRoleIds);

                var menuItemModel = new MenuViewModel.MenuItem
                {
                    Text = text,
                    Title = title,
                    Url = url,
                    ViewRoles = menuItemRoles.Select(x => x.Name)
                };

                menuItemModel.Children.AddRange(PopulateMenuItems(context, menuItems, menuItem.Id, language, addLanguageSlug));

                result.Add(menuItemModel);
            }

            return result;
        }

        private void LoadMenuItems(WeapsyDbContext context, Menu menu)
        {
            if (menu == null)
                return;

            menu.MenuItems = context.MenuItems
                .Include(x => x.MenuItemLocalisations)
                .Include(x => x.MenuItemPermissions)
                .Where(x => x.MenuId == menu.Id && x.Status != MenuItemStatus.Deleted)
                .ToList();
        }
    }
}
